using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Core;

/// <summary>
/// Developer command-line hooks (autoload). Arguments go after "--" on the Godot command line:
///   --scene=res://path.tscn   scene to open instead of the default (read by Boot)
///   --capture=C:/out/shot.png save a screenshot of the main viewport, then quit
///   --capture-frame=N         frame at which to capture (default 120)
///   --quit-after=N            quit after N frames (for smoke tests)
/// </summary>
public partial class DevTools : Node
{
    private readonly Dictionary<string, string> _args = new();
    private string? _capturePath;
    private int _captureFrame = 120;
    private int _quitAfter = -1;
    private int _frame;

    public static DevTools? Instance { get; private set; }

    public string? Arg(string key) => _args.TryGetValue(key, out var v) ? v : null;

    public override void _EnterTree()
    {
        Instance = this;
        foreach (string raw in OS.GetCmdlineUserArgs())
        {
            string a = raw.TrimStart('-');
            int eq = a.IndexOf('=');
            if (eq > 0) _args[a[..eq]] = a[(eq + 1)..];
            else _args[a] = "true";
        }

        _capturePath = Arg("capture");
        if (int.TryParse(Arg("capture-frame"), out int cf)) _captureFrame = cf;
        if (int.TryParse(Arg("quit-after"), out int qa)) _quitAfter = qa;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        var version = Engine.GetVersionInfo();
        GD.Print($"[DevTools] Godot {version["string"]} | renderer {RenderingServer.GetCurrentRenderingDriverName()} " +
                 $"| GPU {RenderingServer.GetVideoAdapterName()} | physics {ProjectSettings.GetSetting("physics/3d/physics_engine")} " +
                 $"@ {Engine.PhysicsTicksPerSecond} Hz");
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_capturePath != null && _frame == _captureFrame)
        {
            // The viewport texture holds the last fully rendered frame.
            SaveCapture();
            return;
        }
        if (_quitAfter > 0 && _frame >= _quitAfter)
        {
            GD.Print($"[DevTools] quit after {_frame} frames");
            GetTree().Quit();
        }
    }

    /// <summary>
    /// Quits after flushing pending .NET finalizers, so no managed wrapper is disposed while the engine shuts down.
    /// </summary>
    public static void QuitClean(SceneTree tree, int exitCode)
    {
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        tree.Quit(exitCode);
    }

    private void SaveCapture()
    {
        var image = GetViewport().GetTexture().GetImage();
        string dir = _capturePath!.GetBaseDir();
        if (!string.IsNullOrEmpty(dir)) DirAccess.MakeDirRecursiveAbsolute(dir);
        var err = image.SavePng(_capturePath);
        GD.Print($"[DevTools] capture {(err == Error.Ok ? "saved" : "FAILED " + err)}: {_capturePath} ({image.GetWidth()}x{image.GetHeight()})");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }
}
