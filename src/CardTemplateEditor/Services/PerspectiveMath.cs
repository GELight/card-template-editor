namespace CardTemplateEditor.Services;

/// <summary>
/// Reine Mathematik für eine 4-Punkt-Perspektivtransformation (projektive
/// Homographie). Wird vom ExportService genutzt, um Text auf ein Vier-Punkt-
/// Quadrilateral zu mappen (3D-Look auf einem 2D-Bild) und vom Editor zur
/// Wireframe-Vorschau der Ziel-Ecken.
///
/// Konvention: src/dst-Punkte werden in fester Reihenfolge angegeben — NW, NE,
/// SE, SW (im Uhrzeigersinn beginnend oben links). Zurückgegeben wird eine
/// 3×3-Matrix in Zeilen-Form (m11, m12, m13, m21, m22, m23, m31, m32, m33).
/// Anwendung auf einen Punkt:
///   X' = (m11*x + m12*y + m13) / (m31*x + m32*y + m33)
///   Y' = (m21*x + m22*y + m23) / (m31*x + m32*y + m33)
/// </summary>
public static class PerspectiveMath
{
    public readonly record struct Pt(double X, double Y);

    /// <summary>
    /// Liefert die 3×3-Homographie M, sodass M(src[i]) = dst[i] für i ∈ [0..3].
    /// </summary>
    /// <remarks>
    /// Implementiert via klassischem Zwischenschritt über das Einheitsquadrat:
    /// 1. Berechne A = src → unit-square-Inverse (Homographie unit→src, dann invertieren)
    /// 2. Berechne B = unit-square → dst (geschlossene Formel, siehe unten)
    /// 3. M = B · A
    /// Das vermeidet das Lösen eines vollen 8×8-LGS und ist numerisch stabiler.
    /// </remarks>
    public static double[] Compute(Pt[] src, Pt[] dst)
    {
        if (src.Length != 4) throw new ArgumentException("src muss 4 Punkte haben.", nameof(src));
        if (dst.Length != 4) throw new ArgumentException("dst muss 4 Punkte haben.", nameof(dst));

        // Homographie unit-square → src (Einheitsquadrat: (0,0), (1,0), (1,1), (0,1))
        var H_unit_to_src = UnitSquareToQuad(src);
        // Homographie unit-square → dst
        var H_unit_to_dst = UnitSquareToQuad(dst);

        // Wir wollen src → dst: also H_unit_to_dst · (H_unit_to_src)^-1.
        var H_src_to_unit = Invert3x3(H_unit_to_src);
        return Multiply3x3(H_unit_to_dst, H_src_to_unit);
    }

    /// <summary>
    /// Wendet die 3×3-Homographie auf einen einzelnen Punkt an.
    /// </summary>
    public static Pt Apply(double[] m, Pt p)
    {
        var w = m[6] * p.X + m[7] * p.Y + m[8];
        if (Math.Abs(w) < 1e-12)
        {
            // Unendlichkeitspunkt — schmeißt der eigentliche Renderer ab; hier
            // eine grobe Annäherung, damit Tests deterministisch bleiben.
            w = 1e-12 * Math.Sign(w == 0 ? 1 : w);
        }
        var x = (m[0] * p.X + m[1] * p.Y + m[2]) / w;
        var y = (m[3] * p.X + m[4] * p.Y + m[5]) / w;
        return new Pt(x, y);
    }

    /// <summary>
    /// Geschlossene Formel für die Homographie, die das Einheitsquadrat
    /// (0,0), (1,0), (1,1), (0,1) auf das übergebene 4-Punkt-Quadrilateral
    /// abbildet (Eckenreihenfolge NW, NE, SE, SW).
    /// </summary>
    private static double[] UnitSquareToQuad(Pt[] q)
    {
        // q[0]=NW (0,0), q[1]=NE (1,0), q[2]=SE (1,1), q[3]=SW (0,1)
        var dx1 = q[1].X - q[2].X;
        var dx2 = q[3].X - q[2].X;
        var sx = q[0].X - q[1].X + q[2].X - q[3].X;
        var dy1 = q[1].Y - q[2].Y;
        var dy2 = q[3].Y - q[2].Y;
        var sy = q[0].Y - q[1].Y + q[2].Y - q[3].Y;

        double g, h;
        if (Math.Abs(sx) < 1e-12 && Math.Abs(sy) < 1e-12)
        {
            // Affiner Fall (Parallelogramm) — keine Perspektive nötig.
            g = 0;
            h = 0;
        }
        else
        {
            var det = dx1 * dy2 - dx2 * dy1;
            if (Math.Abs(det) < 1e-12)
                throw new InvalidOperationException(
                    "Entartetes Quadrilateral — drei Punkte kollinear oder identisch.");
            g = (sx * dy2 - dx2 * sy) / det;
            h = (dx1 * sy - sx * dy1) / det;
        }

        var a = q[1].X - q[0].X + g * q[1].X;
        var b = q[3].X - q[0].X + h * q[3].X;
        var c = q[0].X;
        var d = q[1].Y - q[0].Y + g * q[1].Y;
        var e = q[3].Y - q[0].Y + h * q[3].Y;
        var f = q[0].Y;

        return new[] { a, b, c, d, e, f, g, h, 1.0 };
    }

    private static double[] Multiply3x3(double[] a, double[] b)
    {
        var r = new double[9];
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            r[i * 3 + j] =
                a[i * 3 + 0] * b[0 * 3 + j] +
                a[i * 3 + 1] * b[1 * 3 + j] +
                a[i * 3 + 2] * b[2 * 3 + j];
        return r;
    }

    private static double[] Invert3x3(double[] m)
    {
        var a = m[0]; var b = m[1]; var c = m[2];
        var d = m[3]; var e = m[4]; var f = m[5];
        var g = m[6]; var h = m[7]; var i = m[8];

        var A =  (e * i - f * h);
        var B = -(d * i - f * g);
        var C =  (d * h - e * g);
        var D = -(b * i - c * h);
        var E =  (a * i - c * g);
        var F = -(a * h - b * g);
        var G =  (b * f - c * e);
        var H = -(a * f - c * d);
        var I =  (a * e - b * d);

        var det = a * A + b * B + c * C;
        if (Math.Abs(det) < 1e-12)
            throw new InvalidOperationException("Matrix nicht invertierbar.");
        var inv = 1.0 / det;
        return new[]
        {
            A * inv, D * inv, G * inv,
            B * inv, E * inv, H * inv,
            C * inv, F * inv, I * inv,
        };
    }
}
