using Godot;

namespace DestructionRacing.Tools;

/// <summary>On-screen key guide for the look-dev scenes (top left, shadowed so it reads on any background).</summary>
public static class KeyGuide
{
    /// <summary>Line break for label text (Environment.NewLine would add a CR, which Godot draws as an extra line).</summary>
    public const string NewLine = "\n";

    public static Label Add(Node parent, string text)
    {
        var layer = new CanvasLayer();
        parent.AddChild(layer);
        var label = new Label { Position = new Vector2(24, 18), Text = text };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.8f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        layer.AddChild(label);
        return label;
    }
}
