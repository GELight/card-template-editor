using System.Threading;
using System.Threading.Tasks;

namespace CardTemplateEditor.Services;

/// <summary>
/// Debounce-Speicher: Mehrere schnell aufeinanderfolgende Trigger() lösen genau einen
/// Save aus, sobald die Debounce-Frist seit dem letzten Trigger verstrichen ist.
/// Die Delay-Funktion ist injizierbar, damit Tests die Zeit kontrollieren können.
/// </summary>
public sealed class AutoSaveService : IAsyncDisposable
{
    private readonly TimeSpan _debounce;
    private readonly Func<CancellationToken, Task> _save;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task _pending = Task.CompletedTask;
    private int _saveCount;

    public AutoSaveService(
        TimeSpan debounce,
        Func<CancellationToken, Task> save,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _debounce = debounce;
        _save = save;
        _delay = delay ?? Task.Delay;
    }

    public int SaveCount => _saveCount;

    public void Trigger()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _pending = RunAsync(token);
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await _delay(_debounce, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;
            await _save(token).ConfigureAwait(false);
            Interlocked.Increment(ref _saveCount);
        }
        catch (OperationCanceledException)
        {
            // Erwartet, wenn ein folgender Trigger() den aktuellen Lauf abbricht.
        }
    }

    public async Task WaitForIdleAsync()
    {
        while (true)
        {
            Task pending;
            lock (_lock) pending = _pending;
            if (pending.IsCompleted) return;
            try { await pending.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>
    /// Bricht einen laufenden Debounce ab OHNE zu speichern.
    /// Wird beim Löschen des aktuellen Templates verwendet, damit der noch ausstehende
    /// Save das eben gelöschte Template nicht ein letztes Mal auf die Platte schreibt.
    /// </summary>
    public void Cancel()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = null;
        }
    }

    /// <summary>
    /// Bricht laufenden Debounce ab und schreibt sofort. Beim Window.Closing aufrufen.
    /// </summary>
    public async Task FlushAsync(CancellationToken token = default)
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = null;
        }
        await _save(token).ConfigureAwait(false);
        Interlocked.Increment(ref _saveCount);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock) _cts?.Cancel();
        await WaitForIdleAsync().ConfigureAwait(false);
    }
}
