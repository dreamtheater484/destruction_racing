using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// One synthesized engine's grains (tools/enginesynth/synth.py): whole engine cycles recorded at a ladder of
/// rpm values, off and on throttle, each with a short tail for crossfading.
/// </summary>
public sealed class GrainBank
{
    public string Name = "";
    public int SampleRate;
    public float Tail;
    public float CycleDeg;
    public float IdleRpm, RedlineRpm;
    public float[] Rpms = Array.Empty<float>();
    public float[] Data = Array.Empty<float>();
    /// <summary>[rpm index, load 0/1] -> (offset, length) of each grain.</summary>
    public (int Offset, int Length)[,][] Tables = new (int, int)[0, 0][];

    private static readonly Dictionary<string, GrainBank> Cache = new();

    public static GrainBank Load(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;
        string dir = "res://assets/audio/engines/";
        using var meta = JsonDocument.Parse(FileAccess.GetFileAsString(dir + name + ".json"));
        var root = meta.RootElement;
        var b = new GrainBank
        {
            Name = name,
            SampleRate = root.GetProperty("sample_rate").GetInt32(),
            Tail = root.GetProperty("tail").GetSingle(),
            CycleDeg = root.GetProperty("cycle_deg").GetSingle(),
            IdleRpm = root.GetProperty("idle_rpm").GetSingle(),
            RedlineRpm = root.GetProperty("redline_rpm").GetSingle(),
        };
        var rpms = new List<float>();
        foreach (var r in root.GetProperty("rpms").EnumerateArray()) rpms.Add(r.GetSingle());
        b.Rpms = rpms.ToArray();
        b.Tables = new (int, int)[b.Rpms.Length, 2][];
        foreach (var t in root.GetProperty("tables").EnumerateArray())
        {
            float rpm = t.GetProperty("rpm").GetSingle();
            int load = t.GetProperty("load").GetInt32();
            int i = Array.FindIndex(b.Rpms, r => MathF.Abs(r - rpm) < 0.01f);
            var grains = new List<(int, int)>();
            foreach (var g in t.GetProperty("grains").EnumerateArray())
                grains.Add((g[0].GetInt32(), g[1].GetInt32()));
            b.Tables[i, load] = grains.ToArray();
        }
        byte[] raw = FileAccess.GetFileAsBytes(dir + name + ".bin");
        b.Data = new float[raw.Length / 4];
        Buffer.BlockCopy(raw, 0, b.Data, 0, raw.Length);
        Cache[name] = b;
        return b;
    }
}

/// <summary>
/// Granular engine voice: per engine cycle it blends the four grains around (rpm, load) - two rpm tables x
/// off/on throttle - each resampled to the current cycle length, and overlap-adds them with a short crossfade.
/// The offline twin is tools/enginesynth/granular.py. Feed Rpm and Load every frame.
/// </summary>
public sealed class GranularVoice
{
    public float Rpm { get; set; }
    public float Load { get; set; }
    public GrainBank Bank { get; private set; }

    private float[] _acc = Array.Empty<float>();
    private long _read, _write;            // absolute sample positions; _acc is a ring
    private readonly Random _rng;
    private int[,] _last = new int[0, 2];
    private float[] _scratch = new float[4096];

    public GranularVoice(GrainBank bank, int seed = 1)
    {
        _rng = new Random(seed);
        Bank = bank;
        SetBank(bank);
        Rpm = bank.IdleRpm;
    }

    /// <summary>
    /// Another engine on this voice, keeping its buffers: the event's voices follow the nearest cars, and a new
    /// voice per switch put a 384 kB ring on the .NET large-object heap each time (full GCs, M8).
    /// </summary>
    public void SetBank(GrainBank bank)
    {
        Bank = bank;
        if (_acc.Length != bank.SampleRate * 2) _acc = new float[bank.SampleRate * 2];
        else Array.Clear(_acc);
        _read = _write = 0;
        if (_last.GetLength(0) != bank.Rpms.Length) _last = new int[bank.Rpms.Length, 2];
        for (int i = 0; i < bank.Rpms.Length; i++) { _last[i, 0] = -1; _last[i, 1] = -1; }
    }

    /// <summary>Writes the next samples (mono) into <paramref name="dst"/>.</summary>
    public void Render(Span<float> dst)
    {
        int n = dst.Length;
        while (_write - _read < n) ScheduleCycle();
        int cap = _acc.Length;
        for (int k = 0; k < n; k++)
        {
            int idx = (int)((_read + k) % cap);
            dst[k] = _acc[idx];
            _acc[idx] = 0f;
        }
        _read += n;
    }

    private void ScheduleCycle()
    {
        var b = Bank;
        float rpm = Math.Clamp(Rpm, b.Rpms[0], b.Rpms[^1]);
        float load = Math.Clamp(Load, 0f, 1f);
        int period = (int)MathF.Round(b.SampleRate * 60f / rpm * b.CycleDeg / 360f);
        int nOut = period + (int)(period * b.Tail);
        if (_scratch.Length < nOut) _scratch = new float[nOut * 2];
        Array.Clear(_scratch, 0, nOut);

        float x = MathF.Log(rpm / b.Rpms[0]) / MathF.Log(b.Rpms[^1] / b.Rpms[0]) * (b.Rpms.Length - 1);
        int i0 = Math.Clamp((int)MathF.Floor(x), 0, b.Rpms.Length - 2);
        float w = Math.Clamp(x - i0, 0f, 1f);
        for (int s = 0; s < 4; s++)
        {
            int i = i0 + (s >> 1);
            int ld = s & 1;
            float wt = ((s >> 1) == 0 ? 1f - w : w) * (ld == 0 ? 1f - load : load);
            if (wt < 1e-3f) continue;
            var grains = b.Tables[i, ld];
            int g = _rng.Next(grains.Length);
            if (g == _last[i, ld]) g = (g + 1) % grains.Length;
            _last[i, ld] = g;
            var (off, len) = grains[g];
            // Linear resample of the grain to nOut samples.
            float step = (len - 1) / (float)(nOut - 1);
            for (int k = 0; k < nOut; k++)
            {
                float p = k * step;
                int j = (int)p;
                float f = p - j;
                int j1 = Math.Min(j + 1, len - 1);
                _scratch[k] += wt * (b.Data[off + j] * (1f - f) + b.Data[off + j1] * f);
            }
        }
        // Raised-cosine crossfade at both ends; the tail overlaps the next cycle's fade-in.
        int fade = nOut - period;
        int cap = _acc.Length;
        for (int k = 0; k < nOut; k++)
        {
            float win = 1f;
            if (k < fade) win = 0.5f - 0.5f * MathF.Cos(MathF.PI * k / fade);
            else if (k >= period) win = 0.5f + 0.5f * MathF.Cos(MathF.PI * (k - period) / fade);
            _acc[(int)((_write + k) % cap)] += _scratch[k] * win;
        }
        _write += period;
    }
}

/// <summary>A 3D engine sound source: a GranularVoice streamed through an AudioStreamGenerator.</summary>
public partial class GranularEngine : AudioStreamPlayer3D
{
    [Export] public string EngineName { get; set; } = "nordvik";
    public float Rpm { get => _voice?.Rpm ?? 0f; set { if (_voice != null) _voice.Rpm = value; } }
    public float Load { get => _voice?.Load ?? 0f; set { if (_voice != null) _voice.Load = value; } }
    public GrainBank? Bank => _voice?.Bank;

    private GranularVoice? _voice;
    private AudioStreamGeneratorPlayback? _playback;
    /// <summary>The generator is fed in blocks of this many frames, so the push buffer is never reallocated.</summary>
    private const int Block = 128;
    private readonly float[] _mono = new float[Block];
    private readonly Vector2[] _frames = new Vector2[Block];

    public override void _Ready()
    {
        var bank = GrainBank.Load(EngineName);
        _voice = new GranularVoice(bank, (int)GetInstanceId());
        Stream = new AudioStreamGenerator { MixRate = bank.SampleRate, BufferLength = 0.08f };
        Play();
        _playback = (AudioStreamGeneratorPlayback)GetStreamPlayback();
    }

    public void SwitchEngine(string name)
    {
        EngineName = name;
        float rpm = Rpm, load = Load;
        var bank = GrainBank.Load(name);
        if (_voice == null) _voice = new GranularVoice(bank, (int)GetInstanceId());
        else _voice.SetBank(bank);
        _voice.Rpm = rpm;
        _voice.Load = load;
    }

    public override void _Process(double delta)
    {
        if (_playback == null || _voice == null) return;
        // Whole blocks only (what is left waits for the next frame: under 3 ms of the 80 ms buffer).
        for (int n = _playback.GetFramesAvailable(); n >= Block; n -= Block)
        {
            _voice.Render(_mono);
            for (int i = 0; i < Block; i++) _frames[i] = new Vector2(_mono[i], _mono[i]);
            _playback.PushBuffer(_frames);
        }
    }
}
