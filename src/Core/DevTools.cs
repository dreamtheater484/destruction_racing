using System.Linq;
using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Core;

/// <summary>
/// Developer command-line hooks (autoload). Arguments go after "--" on the Godot command line:
///   --scene=res://path.tscn   scene to open instead of the default (read by Boot)
///   --capture=C:/out/shot.png save a screenshot of the main viewport, then quit
///   --capture-frame=N         frame at which to capture (default 120)
///   --quit-after=N            quit after N frames (for smoke tests)
///   --perf[=S]                print frame time (mean, 1% low), draw calls, triangles and VRAM every S s
///   --shot-every=S --shot-dir=out/x   save a screenshot every S seconds (stress scenes)
///   --keys=Enter,Down,w2,Escape   press keys one by one from 1.5 s on, across scene changes (wN waits N × 0.5 s;
///                             --menukeys is the same)
/// </summary>
public partial class DevTools : Node
{
    private readonly Dictionary<string, string> _args = new();
    private string? _capturePath;
    private int _captureFrame = 120;
    private int _quitAfter = -1;
    private int _frame;
    private float _perfEvery, _perfClock, _shotEvery, _shotClock;
    private string _shotDir = "";
    private readonly List<double> _frameTimes = new();
    private int _shotIndex;

    public static DevTools? Instance { get; private set; }

    /// <summary>--loadtime (plan §17.4: restart to driving ≤ 3 s, load ≤ 5 s): when the last scene change was asked for.</summary>
    public static ulong SceneRequestedAt { get; private set; }
    public static int SceneLoads { get; set; }
    public static void MarkSceneRequest() => SceneRequestedAt = Time.GetTicksMsec();

    public string? Arg(string key) => _args.TryGetValue(key, out var v) ? v : null;

    public override void _EnterTree()
    {
        Instance = this;
        if (OS.GetCmdlineUserArgs().Any(x => x.StartsWith("--loadtime"))) GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"[Load] engine up: {Time.GetTicksMsec() / 1000.0:0.00} s after launch"));
        // The game's text is English: "4.9 s", never the system locale's "4,9 s".
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
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
        if (Arg("perf") is { } perf) _perfEvery = float.TryParse(perf, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pe) ? pe : 5f;
        if (float.TryParse(Arg("shot-every"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float se)) _shotEvery = se;
        _shotDir = Arg("shot-dir") ?? "out/shots/series";
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        if ((Arg("keys") ?? Arg("menukeys")) is { } keys) FeedKeys(keys);
        var version = Engine.GetVersionInfo();
        GD.Print($"[DevTools] Godot {version["string"]} | renderer {RenderingServer.GetCurrentRenderingDriverName()} " +
                 $"| GPU {RenderingServer.GetVideoAdapterName()} | physics {ProjectSettings.GetSetting("physics/3d/physics_engine")} " +
                 $"@ {Engine.PhysicsTicksPerSecond} Hz");
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_perfEvery > 0f) Perf(delta);
        if (_shotEvery > 0f && (_shotClock += (float)delta) >= _shotEvery)
        {
            _shotClock = 0f;
            string dir = ProjectSettings.GlobalizePath(_shotDir.StartsWith("res://") ? _shotDir : "res://" + _shotDir);
            DirAccess.MakeDirRecursiveAbsolute(dir);
            GetViewport().GetTexture().GetImage().SavePng($"{dir}/shot_{++_shotIndex:000}.png");
        }
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

    private async void FeedKeys(string script)
    {
        var tree = GetTree();
        await ToSignal(tree.CreateTimer(1.5, true, false, true), SceneTreeTimer.SignalName.Timeout);
        foreach (string raw in script.Split(','))
        {
            string token = raw.Trim();
            if (token.StartsWith('w') && int.TryParse(token[1..], out int halves))
            {
                await ToSignal(tree.CreateTimer(halves * 0.5, true, false, true), SceneTreeTimer.SignalName.Timeout);
                continue;
            }
            if (!System.Enum.TryParse(token, true, out Key key)) { GD.PushWarning($"[DevTools] unknown key {token}"); continue; }
            var top = tree.CurrentScene is Menus.FrontEnd fe ? fe.Top.GetType().Name : tree.CurrentScene?.Name.ToString() ?? "?";
            GD.Print($"[FrontEnd] key {key} on {top}");
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
            await ToSignal(tree.CreateTimer(0.4, true, false, true), SceneTreeTimer.SignalName.Timeout);
        }
    }

    private void Perf(double delta)
    {
        if (_frame < 30) return;                      // skip loading hitches
        _frameTimes.Add(delta * 1000.0);
        _perfClock += (float)delta;
        if (_perfClock < _perfEvery) return;
        _frameTimes.Sort();
        double mean = 0;
        foreach (double t in _frameTimes) mean += t;
        mean /= _frameTimes.Count;
        double p99 = _frameTimes[(int)(_frameTimes.Count * 0.99)];
        GD.Print($"[Perf] {_frameTimes.Count} frames: mean {mean:0.00} ms ({1000.0 / mean:0} fps), 1% low {1000.0 / p99:0} fps, " +
                 $"draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}, " +
                 $"triangles {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1e6:0.00} M, " +
                 $"VRAM {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:0} MB");
        _frameTimes.Clear();
        _perfClock = 0f;
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
