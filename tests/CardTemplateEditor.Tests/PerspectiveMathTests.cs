using CardTemplateEditor.Services;

namespace CardTemplateEditor.Tests;

public class PerspectiveMathTests
{
    private const double Eps = 1e-6;

    private static PerspectiveMath.Pt P(double x, double y) => new(x, y);

    [Fact]
    public void Compute_IdentityWhenSrcEqualsDst_MapsEachPointToItself()
    {
        var rect = new[] { P(0, 0), P(100, 0), P(100, 50), P(0, 50) };
        var m = PerspectiveMath.Compute(rect, rect);

        foreach (var p in rect)
        {
            var q = PerspectiveMath.Apply(m, p);
            Assert.Equal(p.X, q.X, precision: 6);
            Assert.Equal(p.Y, q.Y, precision: 6);
        }
        // Auch ein interner Punkt landet wieder bei sich selbst.
        var inner = PerspectiveMath.Apply(m, P(37.5, 12.3));
        Assert.Equal(37.5, inner.X, precision: 6);
        Assert.Equal(12.3, inner.Y, precision: 6);
    }

    [Fact]
    public void Compute_PureTranslation_TranslatesAllPoints()
    {
        var src = new[] { P(0, 0), P(10, 0), P(10, 10), P(0, 10) };
        var dst = new[] { P(5, 7), P(15, 7), P(15, 17), P(5, 17) };
        var m = PerspectiveMath.Compute(src, dst);

        var q = PerspectiveMath.Apply(m, P(5, 5));
        Assert.Equal(10, q.X, precision: 6);
        Assert.Equal(12, q.Y, precision: 6);
    }

    [Fact]
    public void Compute_FullPerspective_MapsCornersExactly()
    {
        // Quadrat → Trapez (klassischer Perspektive-Effekt: obere Kante schmaler).
        var src = new[] { P(0, 0), P(100, 0), P(100, 100), P(0, 100) };
        var dst = new[] { P(20, 0), P(80, 0), P(100, 100), P(0, 100) };
        var m = PerspectiveMath.Compute(src, dst);

        for (var i = 0; i < 4; i++)
        {
            var q = PerspectiveMath.Apply(m, src[i]);
            Assert.Equal(dst[i].X, q.X, precision: 6);
            Assert.Equal(dst[i].Y, q.Y, precision: 6);
        }
    }

    [Fact]
    public void Compute_FullPerspective_InteriorPointFollowsVanishingPointRule()
    {
        // Trapezoid mit schmalerem Top (Fluchtpunkt OBERHALB der Form): die
        // untere Kante ist visuell "näher" und nimmt mehr Raum ein, die obere
        // wirkt komprimiert. Daher landet der Mittelpunkt (0.5, 0.5) des
        // Quellquadrats NICHT bei y'=0.5, sondern oberhalb (näher zum schmalen
        // Top). Geschlossen: y' = 1/3 für diesen klassischen Trapezoidaufbau.
        var src = new[] { P(0, 0), P(1, 0), P(1, 1), P(0, 1) };
        var dst = new[] { P(0.25, 0), P(0.75, 0), P(1, 1), P(0, 1) };
        var m = PerspectiveMath.Compute(src, dst);

        var q = PerspectiveMath.Apply(m, P(0.5, 0.5));
        Assert.Equal(0.5, q.X, precision: 6);
        Assert.Equal(1.0 / 3.0, q.Y, precision: 6);
    }

    [Fact]
    public void Compute_AllDstPointsCollapsedToOne_AllInputsMapToThatPoint()
    {
        // Entartet aber nicht "throw": wenn alle vier Ziel-Ecken auf einem
        // Punkt liegen, ist die Homographie degeneriert und mappt jeden Input
        // auf diesen einen Punkt. Wir verifizieren: keine Exception, aber die
        // Mappings sind sinnvoll degeneriert.
        var src = new[] { P(0, 0), P(10, 0), P(10, 10), P(0, 10) };
        var dst = new[] { P(5, 5), P(5, 5), P(5, 5), P(5, 5) };
        var m = PerspectiveMath.Compute(src, dst);

        var q = PerspectiveMath.Apply(m, P(2, 7));
        Assert.Equal(5, q.X, precision: 6);
        Assert.Equal(5, q.Y, precision: 6);
    }

    [Fact]
    public void Compute_AffineParallelogram_MatchesPureAffineMapping()
    {
        // Parallelogramm (Σ = 0) → kein Perspektive-Anteil, reine affine Map.
        var src = new[] { P(0, 0), P(10, 0), P(10, 10), P(0, 10) };
        var dst = new[] { P(5, 5), P(15, 5), P(20, 15), P(10, 15) };
        var m = PerspectiveMath.Compute(src, dst);

        // m[6] und m[7] (g, h) müssen 0 sein → keine Perspektive.
        Assert.Equal(0, m[6], precision: 6);
        Assert.Equal(0, m[7], precision: 6);

        // Punkt (5, 5) → erwartet (12.5, 10) per linearer Interpolation.
        var q = PerspectiveMath.Apply(m, P(5, 5));
        Assert.Equal(12.5, q.X, precision: 6);
        Assert.Equal(10, q.Y, precision: 6);
    }
}
