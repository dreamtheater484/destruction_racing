using Godot;

namespace DestructionRacing.UI;

/// <summary>
/// Full-screen effects (plan §8.4, §11), on canvas layer 5: above the 3D view, below the HUDs (layer 7).
/// <see cref="Flash"/> for a chromatic split on big hits, <see cref="Pulse"/> for a radial blur from a huge one,
/// <see cref="SlowMo"/> for the slow-motion grade, and with the FILM LOOK option a fine grain and a soft vignette
/// all the time. Without any of them the overlay doesn't draw.
/// </summary>
public partial class ScreenFx : CanvasLayer
{
    private ColorRect _rect = null!;
    private ShaderMaterial _material = null!;
    private float _chroma, _slowmo, _slowmoTarget, _pulse;
    // Set every frame: made once (a string makes a new StringName per call, garbage for the GC).
    private static readonly StringName Chroma = "chroma", Slowmo = "slowmo", PulseParam = "pulse", Film = "film", TimeParam = "time_s";

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

    /// <summary>A radial blur (0..1) out from a point on the screen (0..1, the hit), fading in a quarter second.</summary>
    public void Pulse(float strength, Vector2 centre)
    {
        strength = Mathf.Clamp(strength, 0f, 1f);
        if (strength < _pulse) return;
        _pulse = strength;
        _material.SetShaderParameter("pulse_centre", centre.Clamp(Vector2.Zero, Vector2.One));
    }

    /// <summary>
    /// A huge hit on the player (plan §11): the flash along the hit's direction on screen and the blur pulse out
    /// from it (from the screen centre when the hit is behind the camera).
    /// </summary>
    public void HugeHit(Vector3 point, float energy)
    {
        var vp = GetViewport();
        var cam = vp.GetCamera3D();
        var size = vp.GetVisibleRect().Size;
        bool seen = cam != null && !cam.IsPositionBehind(point);
        var screen = seen ? cam!.UnprojectPosition(point) : size * 0.5f;
        Flash(Mathf.Clamp(energy / 160_000f, 0.5f, 1f), seen ? screen - size * 0.5f : Vector2.Right);
        Pulse(Mathf.Clamp(0.45f + (energy - 90_000f) / 150_000f, 0.45f, 1f), screen / size);
    }

    /// <summary>The slow-motion grade on or off (it fades).</summary>
    public void SlowMo(bool on) => _slowmoTarget = on ? 1f : 0f;

    public override void _Process(double delta)
    {
        float rdt = (float)(delta / System.Math.Max(Engine.TimeScale, 0.01));
        _chroma = Mathf.Max(0f, _chroma - rdt * 7f);
        _pulse = Mathf.Max(0f, _pulse - rdt * 4f);
        _slowmo = Mathf.MoveToward(_slowmo, _slowmoTarget, rdt * 4f);
        bool film = Core.GameSettings.FilmLook;
        bool active = film || _chroma > 0.001f || _slowmo > 0.001f || _pulse > 0.001f;
        _rect.Visible = active;
        if (!active) return;
        _material.SetShaderParameter(Chroma, _chroma);
        _material.SetShaderParameter(Slowmo, _slowmo);
        // Ease out, so the blur snaps in and lets go gently.
        _material.SetShaderParameter(PulseParam, _pulse * _pulse);
        _material.SetShaderParameter(Film, film ? 1f : 0f);
        _material.SetShaderParameter(TimeParam, (float)(Time.GetTicksMsec() % 100000) / 1000f);
    }
}
