using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Tools;

/// <summary>
/// Dev A/B for the GPU budget (<c>--perf=S --ab=dof,smoke,...</c>): every other perf window turns one feature off
/// (the next in the list each time) and the window after turns it back on, so neighbouring windows show what the
/// feature costs in the same stretch of the event. Features: dof, smoke, shadows (positional), soft (positional
/// soft shadows to hard), lights (omni and spot), particles, ssao, ssil, ssr, vfog, glow, blur (the compositor's
/// motion blur), film (the screen effects layer), debris (the parts on the floor), debrisshadow (their shadows).
/// </summary>
public static class PerfAb
{
    private static readonly List<Node> Hidden = new();
    private static RenderingServer.ShadowQuality? _soft;
    private static Compositor? _compositor;
    private static readonly Dictionary<string, bool> Env = new();
    private static readonly Dictionary<GeometryInstance3D, GeometryInstance3D.ShadowCastingSetting> Shadows = new();

    private static bool Under<T>(Node n) where T : Node
    {
        for (var p = n.GetParent(); p != null; p = p.GetParent()) if (p is T) return true;
        return false;
    }

    /// <summary>Sets window <paramref name="window"/>'s state and returns its label.</summary>
    public static string Step(SceneTree tree, string[] features, int window)
    {
        Restore(tree);
        if (window % 2 == 0) return "base";
        string name = features[(window / 2) % features.Length];
        Off(tree, name);
        return name + " off";
    }

    private static void Off(SceneTree tree, string name)
    {
        Node root = tree.CurrentScene ?? tree.Root;
        switch (name)
        {
            case "dof": Fx.Focus.Off = true; break;
            case "smoke": foreach (Node n in tree.GetNodesInGroup(Vehicles.CarVisual.SmokeGroup)) Hide(n); break;
            case "soft":
                _soft = Core.GameSettings.PositionalSoftShadow;
                RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.Hard);
                break;
            case "blur":
                if (Environment(root) is { } we) { _compositor = we.Compositor; we.Compositor = null; }
                break;
            case "ssao" or "ssil" or "ssr" or "vfog" or "glow":
                if (Environment(root)?.Environment is { } env) { Env[name] = Flag(env, name); SetFlag(env, name, false); }
                break;
        }
        foreach (var n in All(tree.Root))
        {
            bool hide = name switch
            {
                "shadows" => n is Light3D { ShadowEnabled: true } and not DirectionalLight3D,
                "lights" => n is OmniLight3D or SpotLight3D,
                "particles" => n is GpuParticles3D,
                "film" => n is UI.ScreenFx,
                "debris" => n is Fx.DebrisPiece,
                "debrisshadow" => n is GeometryInstance3D { CastShadow: not GeometryInstance3D.ShadowCastingSetting.Off } && Under<Fx.DebrisPiece>(n),
                _ => false,
            };
            if (!hide) continue;
            if (name == "shadows") { ((Light3D)n).ShadowEnabled = false; Hidden.Add(n); }
            else if (name == "debrisshadow") { var g = (GeometryInstance3D)n; Shadows[g] = g.CastShadow; g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; }
            else Hide(n);
        }
    }

    private static void Hide(Node n)
    {
        switch (n)
        {
            case Node3D { Visible: true } n3: n3.Visible = false; Hidden.Add(n); break;
            case CanvasLayer { Visible: true } c: c.Visible = false; Hidden.Add(n); break;
        }
    }

    private static void Restore(SceneTree tree)
    {
        Fx.Focus.Off = false;
        foreach (var n in Hidden)
        {
            if (!GodotObject.IsInstanceValid(n)) continue;
            switch (n)
            {
                case Light3D l when !l.ShadowEnabled && l.Visible: l.ShadowEnabled = true; break;
                case Node3D n3: n3.Visible = true; break;
                case CanvasLayer c: c.Visible = true; break;
            }
        }
        Hidden.Clear();
        foreach (var (g, c) in Shadows) if (GodotObject.IsInstanceValid(g)) g.CastShadow = c;
        Shadows.Clear();
        if (_soft is { } q) RenderingServer.PositionalSoftShadowFilterSetQuality(q);
        _soft = null;
        Node root = tree.CurrentScene ?? tree.Root;
        if (_compositor != null && Environment(root) is { } we) we.Compositor = _compositor;
        _compositor = null;
        if (Environment(root)?.Environment is { } env)
            foreach (var (k, v) in Env) SetFlag(env, k, v);
        Env.Clear();
    }

    private static bool Flag(Godot.Environment e, string name) => name switch
    {
        "ssao" => e.SsaoEnabled, "ssil" => e.SsilEnabled, "ssr" => e.SsrEnabled, "vfog" => e.VolumetricFogEnabled, _ => e.GlowEnabled,
    };

    private static void SetFlag(Godot.Environment e, string name, bool on)
    {
        switch (name)
        {
            case "ssao": e.SsaoEnabled = on; break;
            case "ssil": e.SsilEnabled = on; break;
            case "ssr": e.SsrEnabled = on; break;
            case "vfog": e.VolumetricFogEnabled = on; break;
            default: e.GlowEnabled = on; break;
        }
    }

    private static WorldEnvironment? Environment(Node root)
    {
        foreach (var n in All(root)) if (n is WorldEnvironment we) return we;
        return null;
    }

    private static IEnumerable<Node> All(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            yield return c;
            foreach (var x in All(c)) yield return x;
        }
    }
}
