using CardTemplateEditor.Models;

namespace CardTemplateEditor.Tests;

public class BoldRangeMathTests
{
    [Fact]
    public void IsBoldAt_ReturnsFalse_WhenNoRangesCoverIndex()
    {
        var ranges = new List<BoldRange>
        {
            new() { Start = 2, Length = 3 }, // 2,3,4
        };
        Assert.False(BoldRangeMath.IsBoldAt(ranges, 0));
        Assert.False(BoldRangeMath.IsBoldAt(ranges, 1));
        Assert.True(BoldRangeMath.IsBoldAt(ranges, 2));
        Assert.True(BoldRangeMath.IsBoldAt(ranges, 4));
        Assert.False(BoldRangeMath.IsBoldAt(ranges, 5));
    }

    [Fact]
    public void ToggleBold_OnEmptyState_AddsRange()
    {
        var result = BoldRangeMath.ToggleBold(new List<BoldRange>(), start: 3, length: 4);
        Assert.Single(result);
        Assert.Equal(3, result[0].Start);
        Assert.Equal(4, result[0].Length);
    }

    [Fact]
    public void ToggleBold_OnAllBoldSelection_RemovesBold()
    {
        // Existierender Bereich [2..7), Toggle auf [3..6) → die mittleren
        // Chars werden entfettet → es bleiben [2..3) und [6..7).
        var current = new List<BoldRange> { new() { Start = 2, Length = 5 } };
        var result = BoldRangeMath.ToggleBold(current, start: 3, length: 3);
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Start);
        Assert.Equal(1, result[0].Length);
        Assert.Equal(6, result[1].Start);
        Assert.Equal(1, result[1].Length);
    }

    [Fact]
    public void ToggleBold_OnMixedSelection_MakesAllBold_AndMergesAdjacent()
    {
        // Existierend [2..4), Toggle [3..7) → alle 3..6 werden bold gemacht;
        // mit [2..4) zusammen ergibt das [2..7).
        var current = new List<BoldRange> { new() { Start = 2, Length = 2 } };
        var result = BoldRangeMath.ToggleBold(current, start: 3, length: 4);
        Assert.Single(result);
        Assert.Equal(2, result[0].Start);
        Assert.Equal(5, result[0].Length);
    }

    [Fact]
    public void ToggleBold_ZeroLength_LeavesRangesUntouched_ButNormalizes()
    {
        var current = new List<BoldRange>
        {
            new() { Start = 5, Length = 2 },
            new() { Start = 1, Length = 3 },
        };
        var result = BoldRangeMath.ToggleBold(current, start: 0, length: 0);
        // Sortiert + nicht-überlappend, keine inhaltlichen Änderungen.
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Start);
        Assert.Equal(3, result[0].Length);
        Assert.Equal(5, result[1].Start);
        Assert.Equal(2, result[1].Length);
    }

    [Fact]
    public void Normalize_MergesOverlappingAndAdjacentRanges()
    {
        var input = new List<BoldRange>
        {
            new() { Start = 5, Length = 3 }, // [5..8)
            new() { Start = 8, Length = 2 }, // [8..10) — adjacent
            new() { Start = 0, Length = 2 }, // [0..2)
            new() { Start = 1, Length = 4 }, // [1..5) — overlaps with first two
        };
        var result = BoldRangeMath.Normalize(input);
        Assert.Single(result);
        Assert.Equal(0, result[0].Start);
        Assert.Equal(10, result[0].Length);
    }

    [Fact]
    public void ToggleBold_TwoSeparateToggles_ProducesIndependentRanges()
    {
        // User selektiert "ABC" und togglet → bold. Selektiert dann "XYZ"
        // weiter hinten und togglet → zwei separate Bereiche.
        var step1 = BoldRangeMath.ToggleBold(new List<BoldRange>(), start: 0, length: 3);
        var step2 = BoldRangeMath.ToggleBold(step1, start: 7, length: 3);
        Assert.Equal(2, step2.Count);
        Assert.Equal(0, step2[0].Start); Assert.Equal(3, step2[0].Length);
        Assert.Equal(7, step2[1].Start); Assert.Equal(3, step2[1].Length);

        // Erneutes Togglen über den ERSTEN Bereich entfernt ihn — der zweite
        // bleibt unangetastet.
        var step3 = BoldRangeMath.ToggleBold(step2, start: 0, length: 3);
        Assert.Single(step3);
        Assert.Equal(7, step3[0].Start);
    }
}
