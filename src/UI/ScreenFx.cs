using Godot;

namespace DestructionRacing.UI;

/// <summary>
/// Full-screen feedback (plan §11): <see cref="Flash"/> for a chromatic split on big hits, <see cref="SlowMo"/> for
/// the slow-motion grade. The overlay only draws while one of them is active.
/// </summary>
public partial class ScreenFx : CanvasLayer
{
    private ColorRect _rect = null!;
    private ShaderMaterial _material = null!;
    private float _chroma, _slowmo, _slowmoTarget;

    public override void _Ready()
    {
        Layer = 5;
        ProcessMode = ProcessModeEnum.Always;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/screen_fx.gdshader") };
        _rect = new ColorRect { Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
    }

    /// <summary>A chromatic split (0..1), offset along the hit's screen direction.</summary>
    public void Flash(float strength, Vector2 direction)
    {
        _chroma = Mathf.Max(_chroma, Mathf.Clamp(strength, 0f, 1f));
        if (direction.LengthSquared() > 1e-4f) _material.SetShaderParameter("impact_dir", direction.Normalized());
    }

    /// <summary>The slow-motion grade on or off (it fades).</summary>
    public void SlowMo(bool on) => _slowmoTarget = on ? 1f : 0f;

    public override void _Process(double delta)
    {
        float rdt = (float)(delta / System.Math.Max(Engine.TimeScale, 0.01));
        _chroma = Mathf.Max(0f, _chroma - rdt * 7f);
        _slowmo = Mathf.MoveToward(_slowmo, _slowmoTarget, rdt * 4f);
        bool active = _chroma > 0.001f || _slowmo > 0.001f;
        _rect.Visible = active;
        if (!active) return;
        _material.SetShaderParameter("chroma", _chroma);
        _material.SetShaderParameter("slowmo", _slowmo);
    }
}
