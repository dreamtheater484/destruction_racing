using System;
using DestructionRacing.Core;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// Granular engine prototype (M2). Up = throttle, D = auto drive through the gears, 1/2/3 = Kitsune rotary,
/// Brougham V8, Nordvik four. Headless check: --render=out/audio renders a scripted run of each engine to WAV
/// through the same C# voice (no audio device needed).
/// </summary>
public partial class EngineTest : Node3D
{
    private static readonly string[] Engines = { "kitsune", "brougham", "nordvik" };
    private GranularEngine _engine = null!;
    private Label _label = null!;
    private bool _drive;
    private float _rpm, _load, _gearTimer;
    private int _gear = 1;

    public override void _Ready()
    {
        if (DevTools.Instance?.Arg("render") is { } dir)
        {
            RenderOffline(dir);
            DevTools.QuitClean(GetTree(), 0);
            return;
        }
        AddChild(new Camera3D { Current = true, Position = new Vector3(0, 1.5f, 4f) });
        _engine = new GranularEngine { EngineName = Engines[0], Position = new Vector3(0, 0.5f, 0), UnitSize = 6f };
        AddChild(_engine);
        _rpm = _engine.Bank?.IdleRpm ?? 900f;
        var layer = new CanvasLayer();
        AddChild(layer);
        _label = new Label { Position = new Vector2(40, 40) };
        _label.AddThemeFontSizeOverride("font_size", 28);
        layer.AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (_engine?.Bank is not { } bank) return;
        float dt = (float)delta;
        bool throttle = Input.IsKeyPressed(Key.Up) || _drive;
        Step(bank, throttle, dt, ref _rpm, ref _load, ref _gear, ref _gearTimer, _drive);
        _engine.Rpm = _rpm;
        _engine.Load = _load;
        _label.Text = $"{bank.Name.ToUpperInvariant()}   {_rpm:0000} rpm   load {_load:0.00}" +
                      (_drive ? $"   gear {_gear}" : "") +
                      "\nUp throttle   D auto drive   1 Kitsune rotary   2 Brougham V8   3 Nordvik four";
    }

    /// <summary>A crude drivetrain: rpm rises under throttle (slower in higher gears), falls when lifting.</summary>
    private static void Step(GrainBank b, bool throttle, float dt, ref float rpm, ref float load, ref int gear,
                             ref float gearTimer, bool drive)
    {
        load += ((throttle ? 1f : 0f) - load) * Math.Min(1f, dt * 18f);
        float span = b.RedlineRpm - b.IdleRpm;
        if (throttle && gearTimer <= 0f)
            rpm += span * (drive ? 0.9f / gear : 1.6f) * dt * (1.1f - 0.5f * (rpm - b.IdleRpm) / span);
        else
            rpm -= (rpm - b.IdleRpm) * 1.6f * dt;
        if (gearTimer > 0f) { gearTimer -= dt; load = 0f; }
        if (drive && rpm > b.RedlineRpm * 0.96f && gear < 5)
        {
            gear++;
            rpm *= 0.68f;
            gearTimer = 0.18f;
        }
        rpm = Math.Clamp(rpm, b.IdleRpm, b.RedlineRpm);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode is Key.Key1 or Key.Key2 or Key.Key3)
        {
            _engine.SwitchEngine(Engines[k.Keycode - Key.Key1]);
            _rpm = _engine.Bank!.IdleRpm;
        }
        if (k.Keycode == Key.D)
        {
            _drive = !_drive;
            _gear = 1;
        }
        if (k.Keycode == Key.Escape) GetTree().Quit();
    }

    private static void RenderOffline(string dir)
    {
        string abs = ProjectSettings.GlobalizePath(dir.StartsWith("res://") ? dir : "res://" + dir);
        DirAccess.MakeDirRecursiveAbsolute(abs);
        foreach (string name in Engines)
        {
            var bank = GrainBank.Load(name);
            var voice = new GranularVoice(bank, 5);
            int sr = bank.SampleRate;
            var samples = new float[sr * 14];
            float rpm = bank.IdleRpm, load = 0f, gearTimer = 0f;
            int gear = 1;
            const int block = 256;
            for (int pos = 0; pos < samples.Length; pos += block)
            {
                float t = pos / (float)sr;
                bool drive = t >= 2f && t < 10f;
                Step(bank, drive, block / (float)sr, ref rpm, ref load, ref gear, ref gearTimer, drive);
                voice.Rpm = rpm;
                voice.Load = load;
                voice.Render(samples.AsSpan(pos, Math.Min(block, samples.Length - pos)));
            }
            float peak = 1e-6f;
            foreach (float s in samples) peak = Math.Max(peak, Math.Abs(s));
            WriteWav($"{abs}/godot_{name}.wav", samples, sr, 0.9f / peak);
            GD.Print($"[EngineTest] {abs}/godot_{name}.wav peak {peak:0.000}");
        }
    }

    private static void WriteWav(string path, float[] x, int sr, float gain)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        int bytes = x.Length * 2;
        f.StoreBuffer(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        f.Store32((uint)(36 + bytes));
        f.StoreBuffer(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        f.Store32(16); f.Store16(1); f.Store16(1); f.Store32((uint)sr); f.Store32((uint)(sr * 2)); f.Store16(2); f.Store16(16);
        f.StoreBuffer(System.Text.Encoding.ASCII.GetBytes("data"));
        f.Store32((uint)bytes);
        foreach (float s in x) f.Store16((ushort)(short)Math.Clamp(s * gain * 32767f, -32768f, 32767f));
    }
}
