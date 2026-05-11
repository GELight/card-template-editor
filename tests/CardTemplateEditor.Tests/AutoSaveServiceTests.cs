using CardTemplateEditor.Services;

namespace CardTemplateEditor.Tests;

public class AutoSaveServiceTests
{
    /// <summary>
    /// Kontrollierbare Delay-Quelle: jede Anfrage hängt so lange, bis der Test sie
    /// freigibt oder das Cancellation-Token sie abbricht.
    /// </summary>
    private sealed class FakeDelay
    {
        private readonly object _lock = new();
        private readonly List<TaskCompletionSource> _pending = new();
        public int Calls { get; private set; }

        public Task DelayAsync(TimeSpan _, CancellationToken token)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                Calls++;
                _pending.Add(tcs);
            }
            token.Register(() => tcs.TrySetCanceled(token));
            return tcs.Task;
        }

        /// <summary>Lässt den jüngsten noch nicht abgebrochenen Delay ablaufen.</summary>
        public bool ReleaseLatest()
        {
            lock (_lock)
            {
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    if (!_pending[i].Task.IsCompleted)
                    {
                        _pending[i].TrySetResult();
                        return true;
                    }
                }
                return false;
            }
        }

        public int LivePending
        {
            get
            {
                lock (_lock)
                {
                    return _pending.Count(p => !p.Task.IsCompleted);
                }
            }
        }
    }

    [Fact]
    public async Task FiveQuickTriggers_ResultIn_OneSave_AfterDebounceElapses()
    {
        var saves = 0;
        var fakeDelay = new FakeDelay();
        await using var service = new AutoSaveService(
            TimeSpan.FromMilliseconds(500),
            _ => { Interlocked.Increment(ref saves); return Task.CompletedTask; },
            fakeDelay.DelayAsync);

        for (var i = 0; i < 5; i++) service.Trigger();

        // 5 Trigger → 5 Delay-Aufrufe, aber 4 davon wurden direkt gecancelt.
        Assert.Equal(5, fakeDelay.Calls);
        Assert.Equal(1, fakeDelay.LivePending);

        Assert.True(fakeDelay.ReleaseLatest());
        await service.WaitForIdleAsync();

        Assert.Equal(1, saves);
        Assert.Equal(1, service.SaveCount);
    }

    [Fact]
    public async Task NoTrigger_ProducesNoSave()
    {
        var saves = 0;
        await using var service = new AutoSaveService(
            TimeSpan.FromMilliseconds(50),
            _ => { Interlocked.Increment(ref saves); return Task.CompletedTask; });

        await service.WaitForIdleAsync();

        Assert.Equal(0, saves);
        Assert.Equal(0, service.SaveCount);
    }

    [Fact]
    public async Task TriggerAfterPreviousSaveCompleted_StartsFreshDebounce()
    {
        var saves = 0;
        var fakeDelay = new FakeDelay();
        await using var service = new AutoSaveService(
            TimeSpan.FromMilliseconds(500),
            _ => { Interlocked.Increment(ref saves); return Task.CompletedTask; },
            fakeDelay.DelayAsync);

        service.Trigger();
        fakeDelay.ReleaseLatest();
        await service.WaitForIdleAsync();
        Assert.Equal(1, saves);

        service.Trigger();
        fakeDelay.ReleaseLatest();
        await service.WaitForIdleAsync();
        Assert.Equal(2, saves);
    }

    [Fact]
    public async Task FlushAsync_BypassesDebounce_AndForcesImmediateSave()
    {
        var saves = 0;
        var fakeDelay = new FakeDelay();
        await using var service = new AutoSaveService(
            TimeSpan.FromMilliseconds(500),
            _ => { Interlocked.Increment(ref saves); return Task.CompletedTask; },
            fakeDelay.DelayAsync);

        service.Trigger();
        Assert.Equal(0, saves);

        await service.FlushAsync();

        Assert.Equal(1, saves);
        Assert.Equal(0, fakeDelay.LivePending); // pending Debounce wurde abgebrochen
    }

    [Fact]
    public async Task SaveCallback_ReceivesData_ViaClosure()
    {
        var observedPayloads = new List<string>();
        var payload = "first";
        var fakeDelay = new FakeDelay();
        await using var service = new AutoSaveService(
            TimeSpan.FromMilliseconds(100),
            _ => { observedPayloads.Add(payload); return Task.CompletedTask; },
            fakeDelay.DelayAsync);

        service.Trigger();
        payload = "second"; // Änderung NACH Trigger, VOR Save
        fakeDelay.ReleaseLatest();
        await service.WaitForIdleAsync();

        // Save liest payload zur Save-Zeit, nicht zur Trigger-Zeit:
        // Das ist wichtig, weil das echte AutoSave den AKTUELLEN Zustand serialisieren soll.
        Assert.Single(observedPayloads);
        Assert.Equal("second", observedPayloads[0]);
    }
}
