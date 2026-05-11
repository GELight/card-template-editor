namespace CardTemplateEditor.Models;

/// <summary>
/// Halb-offenes Index-Intervall [Start, Start+Length) im Text eines Textfelds,
/// dessen Glyphen fett gerendert werden sollen. Per Liste am Modell hinterlegt;
/// sortiert/disjoint nach jeder Toggle-Operation. Wird von <see cref="BoldRangeMath.ToggleBold"/>
/// gepflegt — Tests sichern die Invarianten.
/// </summary>
public class BoldRange
{
    public int Start { get; set; }
    public int Length { get; set; }
}

/// <summary>
/// Reine Bereichs-Mathematik für Bold-Toggle, getrennt vom Modell, damit sie
/// ohne UI/Avalonia-Abhängigkeit gegen Edge-Cases (Mixed-Bold-Selection, Range-
/// Splits, Konsolidierung benachbarter Bereiche) testbar ist.
/// </summary>
public static class BoldRangeMath
{
    /// <summary>
    /// Liefert true, wenn ein beliebiger Bereich in <paramref name="ranges"/>
    /// den Index <paramref name="i"/> abdeckt.
    /// </summary>
    public static bool IsBoldAt(IReadOnlyList<BoldRange> ranges, int i)
    {
        foreach (var r in ranges)
            if (i >= r.Start && i < r.Start + r.Length) return true;
        return false;
    }

    /// <summary>
    /// Word/Photoshop-Toggle: sind ALLE Indizes in [start, start+length)
    /// bereits fett, werden sie entfettet — sonst werden alle gefettet
    /// (Mixed-Selection wird zu komplett-fett, gleiches Verhalten wie in
    /// allen üblichen Textverarbeitungen). Liefert eine neue, normalisierte
    /// Liste zurück (sortiert nach Start, keine überlappenden oder
    /// benachbarten Einträge).
    /// </summary>
    public static List<BoldRange> ToggleBold(
        IReadOnlyList<BoldRange> current, int start, int length)
    {
        if (length <= 0) return Normalize(current);
        var end = start + length;

        // Schritt 1: char-bool-Maske über alle relevanten Indizes aufbauen.
        var maskEnd = end;
        foreach (var r in current)
            maskEnd = Math.Max(maskEnd, r.Start + r.Length);
        var bold = new bool[maskEnd];
        foreach (var r in current)
            for (var i = r.Start; i < r.Start + r.Length && i < maskEnd; i++) bold[i] = true;

        // Schritt 2: feststellen, ob die ganze Selection bereits fett ist.
        var allBold = true;
        for (var i = start; i < end; i++)
            if (!bold[i]) { allBold = false; break; }

        // Schritt 3: alle Selection-Chars auf den invertierten Wert setzen.
        var newValue = !allBold;
        for (var i = start; i < end; i++) bold[i] = newValue;

        // Schritt 4: zurück zu Bereichen konsolidieren.
        var result = new List<BoldRange>();
        var idx = 0;
        while (idx < bold.Length)
        {
            if (!bold[idx]) { idx++; continue; }
            var s = idx;
            while (idx < bold.Length && bold[idx]) idx++;
            result.Add(new BoldRange { Start = s, Length = idx - s });
        }
        return result;
    }

    /// <summary>Sortiert nach Start, mergt überlappende/benachbarte Bereiche.</summary>
    public static List<BoldRange> Normalize(IReadOnlyList<BoldRange> ranges)
    {
        if (ranges.Count == 0) return new List<BoldRange>();
        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var result = new List<BoldRange> { new() { Start = sorted[0].Start, Length = sorted[0].Length } };
        for (var i = 1; i < sorted.Count; i++)
        {
            var last = result[^1];
            var lastEnd = last.Start + last.Length;
            var cur = sorted[i];
            if (cur.Start <= lastEnd)
                last.Length = Math.Max(lastEnd, cur.Start + cur.Length) - last.Start;
            else
                result.Add(new BoldRange { Start = cur.Start, Length = cur.Length });
        }
        return result;
    }
}
