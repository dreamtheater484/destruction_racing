using Godot;

namespace DestructionRacing.Core;

/// <summary>Entry scene: opens the scene given with --scene=..., otherwise the default scene.</summary>
public partial class Boot : Node
{
    private const string DefaultScene = "res://scenes/test/DriveTest.tscn";

    public override void _Ready()
    {
        string scene = DevTools.Instance?.Arg("scene") ?? DefaultScene;
        GD.Print($"[Boot] loading {scene}");
        CallDeferred(MethodName.Open, scene);
    }

    private void Open(string scene)
    {
        var err = GetTree().ChangeSceneToFile(scene);
        if (err != Error.Ok)
        {
            GD.PushError($"[Boot] could not open {scene}: {err}");
            GetTree().Quit(1);
        }
    }
}
