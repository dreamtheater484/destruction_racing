using Godot;

namespace DestructionRacing.UI;

/// <summary>
/// A score popup in the world (plan §5.2: "SPIN 180 +25" over the car you spun, beside the HUD stack): it rises
/// from above the car and fades, following the car, in real time (slow motion doesn't hold it up).
/// </summary>
public partial class WorldPopup : Label3D
{
    private Node3D _target = null!;
    private double _age;
    private const double Life = 1.5;

    public static void Show(Node parent, Node3D target, string text, Color color)
    {
        if (!IsInstanceValid(target)) return;
        var p = new WorldPopup
        {
            _target = target,
            Text = text,
            Modulate = color,
            Font = HudStyle.Font("ExtraBold"),
            FontSize = 96,
            OutlineSize = 22,
            OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
            PixelSize = 0.0042f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FixedSize = false,
            Shaded = false,
            RenderPriority = 10,
            OutlineRenderPriority = 9,
            TopLevel = true,
            ProcessMode = ProcessModeEnum.Always,
        };
        parent.AddChild(p);
        p.Follow();
    }

    private void Follow()
    {
        float k = (float)(_age / Life);
        GlobalPosition = _target.GlobalPosition + Vector3.Up * (2.0f + 1.4f * k);
        float pop = k < 0.08f ? 1.25f - 3f * k : 1f;      // a quick pop in
        Scale = Vector3.One * pop;
        var c = Modulate;
        Modulate = new Color(c.R, c.G, c.B, k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f);
        OutlineModulate = new Color(0f, 0f, 0f, 0.85f * Modulate.A);
    }

    public override void _Process(double delta)
    {
        _age += delta / System.Math.Max(Engine.TimeScale, 0.01);
        if (_age >= Life || !IsInstanceValid(_target)) { QueueFree(); return; }
        Follow();
    }
}
