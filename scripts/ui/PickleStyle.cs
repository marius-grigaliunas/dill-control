using System.Globalization;
using Godot;

namespace DillControl.UI;

/// <summary>
/// The look of the game's own readouts -- the health jar and the pickle tally
/// -- as one palette and one font, so the two read as one HUD.
///
/// Goofy on purpose: the enemies are pickles. Chunky outlined lettering in a
/// cartoon face, brine green and jar-lid red, everything a little tilted and
/// bouncing when it changes. The debug overlay and the tuning panel stay plain,
/// because they are instruments and this is the game.
///
/// The font is a <see cref="SystemFont"/> asking for a comic face by name, not
/// a committed font file: no asset pipeline yet, the same argument the
/// synthesised sounds make. A machine without any of the names gets the
/// system default, still outlined, which is legible and merely less silly.
/// PLACEHOLDER: a real display font arrives with the art pass.
/// </summary>
public static class PickleStyle
{
    public static readonly Color Brine = new(0.55f, 0.85f, 0.22f);
    public static readonly Color BrineDeep = new(0.24f, 0.55f, 0.12f);
    public static readonly Color BrineSour = new(0.92f, 0.78f, 0.18f);
    public static readonly Color Lid = new(0.86f, 0.22f, 0.16f);
    public static readonly Color Cream = new(1.0f, 0.97f, 0.86f);
    public static readonly Color Ink = new(0.07f, 0.09f, 0.04f);
    public static readonly Color Glass = new(0.82f, 0.95f, 0.88f, 0.16f);

    private static Font _font;

    /// <summary>
    /// The display face: bold comic lettering for every number, title and
    /// stamp the game draws. Its body-text sibling, the same family at regular
    /// weight, is the project's default theme font
    /// (<c>gui/theme/custom_font</c> = resources/ui/font_body.tres), so every
    /// Label and Button in the game -- menus, prompts, panels -- is the same
    /// family, and nothing is the system sans. Both are SystemFont resources
    /// asking for a comic face by name, data rather than code.
    /// </summary>
    public static Font Font => _font ??= GD.Load<Font>("res://resources/ui/font_display.tres")
        ?? new SystemFont { FontNames = new[] { "Comic Sans MS" }, FontWeight = 800 };

    /// <summary>
    /// A count with thousands grouped by commas -- 6,138 -- whatever the
    /// machine's locale. "N0" alone follows the current culture, which on a
    /// Lithuanian or German desktop prints 6.138 or 6 138 and reads as a
    /// decimal or as two numbers.
    /// </summary>
    public static string Count(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Outline width for a size: about an eighth of the glyph height, never
    /// under two pixels. A fixed width that suits 52 px lettering fills the
    /// counters of 16 px text with ink and makes it illegible.
    /// </summary>
    public static int OutlineFor(int size)
    {
        return Mathf.Clamp(Mathf.RoundToInt(size * 0.13f), 2, 12);
    }

    /// <summary>Width of a string at a size, for centring.</summary>
    public static float Measure(string text, int size)
    {
        return Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
    }

    /// <summary>
    /// Outlined text with its baseline-left at <paramref name="position"/>. The
    /// outline is ink at the fill's alpha, so a fading label fades whole, and
    /// left at -1 it scales with the size (<see cref="OutlineFor"/>).
    /// </summary>
    public static void Text(CanvasItem canvas, Vector2 position, string text, int size, Color fill, int outline = -1)
    {
        if (outline < 0)
        {
            outline = OutlineFor(size);
        }

        Color ink = Ink with { A = fill.A };
        canvas.DrawStringOutline(Font, position, text, HorizontalAlignment.Left, -1, size, outline, ink);
        canvas.DrawString(Font, position, text, HorizontalAlignment.Left, -1, size, fill);
    }

    /// <summary>
    /// Text centred on <paramref name="centre"/>, turned and scaled about it.
    /// The transform is reset afterwards, so callers never inherit a tilt.
    /// </summary>
    public static void TextCentred(
        CanvasItem canvas, Vector2 centre, string text, int size, Color fill,
        float angle = 0.0f, float scale = 1.0f, int outline = -1)
    {
        float width = Measure(text, size);
        canvas.DrawSetTransform(centre, angle, new Vector2(scale, scale));
        Text(canvas, new Vector2(-width * 0.5f, size * 0.36f), text, size, fill, outline);
        canvas.DrawSetTransform(Vector2.Zero, 0.0f, Vector2.One);
    }

    /// <summary>
    /// A comic panel: a parallelogram leaning by <paramref name="skew"/> pixels
    /// (top edge shifted right), a thick ink border, and a hard ink shadow
    /// offset down and right. The building block for every game panel, in
    /// place of a dark rounded rectangle, which reads as a debug box.
    /// </summary>
    public static void Plate(CanvasItem canvas, Rect2 rect, Color fill, float skew = 14.0f, float border = 4.0f)
    {
        void Skewed(Rect2 r, float k, Color colour) => Quad(
            canvas,
            r.Position + new Vector2(k, 0),
            new Vector2(r.End.X + k, r.Position.Y),
            r.End,
            new Vector2(r.Position.X, r.End.Y),
            colour);

        Color ink = Ink with { A = fill.A <= 0.0f ? 0.0f : 1.0f };
        Skewed(new Rect2(rect.Position + new Vector2(6, 6), rect.Size), skew, Ink with { A = 0.55f * ink.A });
        Skewed(rect.Grow(border), skew, ink);
        Skewed(rect, skew, fill);
    }

    // ---------------- Cheap shapes ----------------
    //
    // In Godot 4's canvas renderer, DrawColoredPolygon, DrawPolyline, DrawCircle,
    // DrawArc and every antialiased line each build and upload a vertex buffer
    // of their own, every time they are called. Measured on the target machine
    // in a fight, the HUD's handful of those -- skewed plates, hit wedges, the
    // brine and the pickles -- were most of two milliseconds a frame. A
    // primitive of up to four points is drawn as a plain command with no buffer
    // of its own, so every shape the HUD redraws per frame is built from these.
    // None of them is antialiased; neither was DrawCircle.

    private static readonly Vector2[] _tri = new Vector2[3];
    private static readonly Vector2[] _quad = new Vector2[4];
    private static readonly Color[] _flat = new Color[1];
    private static readonly Vector2[] _noUv = System.Array.Empty<Vector2>();

    /// <summary>A convex quad, corners in order round it.</summary>
    public static void Quad(CanvasItem canvas, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color colour)
    {
        _quad[0] = a;
        _quad[1] = b;
        _quad[2] = c;
        _quad[3] = d;
        _flat[0] = colour;
        canvas.DrawPrimitive(_quad, _flat, _noUv);
    }

    public static void Triangle(CanvasItem canvas, Vector2 a, Vector2 b, Vector2 c, Color colour)
    {
        _tri[0] = a;
        _tri[1] = b;
        _tri[2] = c;
        _flat[0] = colour;
        canvas.DrawPrimitive(_tri, _flat, _noUv);
    }

    /// <summary>
    /// A closed outline filled as triangles from <paramref name="centre"/>,
    /// which every edge of the outline must be visible from -- true of any
    /// convex shape from inside it, and of a liquid's wavy top from the middle
    /// of its floor.
    /// </summary>
    public static void Fan(CanvasItem canvas, Vector2 centre, System.ReadOnlySpan<Vector2> loop, Color colour)
    {
        for (int i = 0; i < loop.Length; i++)
        {
            Triangle(canvas, centre, loop[i], loop[(i + 1) % loop.Length], colour);
        }
    }

    /// <summary>A filled circle as a fan of quads.</summary>
    public static void Disc(CanvasItem canvas, Vector2 centre, float radius, Color colour, int segments = 16)
    {
        Vector2 Rim(int i)
        {
            float a = Mathf.Tau * i / segments;
            return centre + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
        }

        Vector2 first = Rim(0);
        for (int i = 1; i + 1 < segments; i += 2)
        {
            Vector2 c = Rim(i + 1);
            Vector2 d = i + 2 <= segments ? Rim(Mathf.Min(i + 2, segments)) : c;
            Quad(canvas, first, Rim(i), c, d, colour);
        }
    }

    /// <summary>
    /// A band along an arc, <paramref name="width"/> wide and centred on
    /// <paramref name="radius"/>, as one quad a segment.
    /// </summary>
    public static void Band(CanvasItem canvas, Vector2 centre, float radius, float from, float to, int segments, Color colour, float width)
    {
        segments = Mathf.Max(1, segments);
        float inner = radius - (width * 0.5f);
        float outer = radius + (width * 0.5f);
        Vector2 previous = new(Mathf.Cos(from), Mathf.Sin(from));
        for (int i = 1; i <= segments; i++)
        {
            float a = Mathf.Lerp(from, to, i / (float)segments);
            Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
            Quad(canvas, centre + (previous * inner), centre + (previous * outer), centre + (dir * outer), centre + (dir * inner), colour);
            previous = dir;
        }
    }

    /// <summary>A 1-to-0 pop: overshoots to 1+amount and settles back to 1.</summary>
    public static float Pop(float remaining01, float amount)
    {
        float t = Mathf.Clamp(remaining01, 0.0f, 1.0f);
        return 1.0f + (amount * t * t);
    }
}
