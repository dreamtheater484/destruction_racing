using System.Collections.Generic;
using Godot;

namespace DestructionRacing.UI;

/// <summary>
/// The broadcast look of the HUD and menus (plan §13): Barlow Condensed, black panels with hazard-yellow accents,
/// slanted lower-thirds. Fonts and the theme are created once and shared.
/// </summary>
public static class HudStyle
{
    public static readonly Color Yellow = new(1f, 0.78f, 0.06f);
    public static readonly Color Red = new(0.95f, 0.18f, 0.12f);
    public static readonly Color Orange = new(1f, 0.5f, 0.15f);
    public static readonly Color Green = new(0.35f, 0.95f, 0.4f);
    public static readonly Color Ink = new(0.025f, 0.025f, 0.03f, 0.86f);
    public static readonly Color Grey = new(0.68f, 0.68f, 0.7f);
    public static readonly Color White = new(0.97f, 0.97f, 0.95f);

    private static readonly Dictionary<string, Font> Fonts = new();
    private static Theme? _theme;
    private static Texture2D? _hazard;

    /// <summary>Barlow Condensed in one of the shipped weights: Medium, SemiBold, Bold, ExtraBold.</summary>
    public static Font Font(string weight = "Bold")
    {
        if (Fonts.TryGetValue(weight, out var f)) return f;
        string path = $"res://assets/fonts/barlow_condensed/BarlowCondensed-{weight}.ttf";
        Font font = ResourceLoader.Exists(path) ? GD.Load<FontFile>(path) : ThemeDB.FallbackFont;
        Fonts[weight] = font;
        return font;
    }

    public static Theme Theme
    {
        get
        {
            if (_theme != null) return _theme;
            _theme = new Theme { DefaultFont = Font("SemiBold"), DefaultFontSize = 24 };
            _theme.SetColor("font_color", "Label", White);
            _theme.SetColor("font_outline_color", "Label", new Color(0f, 0f, 0f, 0.85f));
            _theme.SetConstant("outline_size", "Label", 6);
            return _theme;
        }
    }

    public static Label Label(string text, int size, Color color, string weight = "Bold", HorizontalAlignment align = HorizontalAlignment.Left, int outline = 6)
    {
        var label = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", Font(weight));
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeConstantOverride("outline_size", outline);
        return label;
    }

    /// <summary>A dark panel with an accent stripe on the left; <paramref name="skew"/> slants it like a TV graphic.</summary>
    public static StyleBoxFlat Panel(Color accent, float skew = 0.18f, int stripe = 6, float alpha = 0.86f) => new()
    {
        BgColor = new Color(Ink.R, Ink.G, Ink.B, alpha),
        BorderColor = accent,
        BorderWidthLeft = stripe,
        Skew = new Vector2(skew, 0f),
        ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 6, ContentMarginBottom = 8,
        AntiAliasing = true,
    };

    /// <summary>Diagonal black-and-yellow hazard stripes (tiles).</summary>
    public static Texture2D Hazard
    {
        get
        {
            if (_hazard != null) return _hazard;
            const int n = 32;
            var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    img.SetPixel(x, y, ((x + y) / (n / 2)) % 2 == 0 ? Yellow : new Color(0.04f, 0.04f, 0.04f));
            _hazard = ImageTexture.CreateFromImage(img);
            return _hazard;
        }
    }

    /// <summary>Anchors a control at a point of its parent (0..1) with a pixel offset and a size.</summary>
    public static T Place<T>(T c, float ax, float ay, Vector2 offset, Vector2 size) where T : Control
    {
        c.AnchorLeft = c.AnchorRight = ax;
        c.AnchorTop = c.AnchorBottom = ay;
        c.OffsetLeft = offset.X;
        c.OffsetTop = offset.Y;
        c.OffsetRight = offset.X + size.X;
        c.OffsetBottom = offset.Y + size.Y;
        c.GrowHorizontal = ax >= 1f ? Control.GrowDirection.Begin : ax <= 0f ? Control.GrowDirection.End : Control.GrowDirection.Both;
        c.GrowVertical = ay >= 1f ? Control.GrowDirection.Begin : Control.GrowDirection.End;
        return c;
    }
}
