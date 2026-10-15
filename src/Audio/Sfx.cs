using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// The synthesized sound set (tools/sfxsynth, assets/audio/sfx/manifest.json): ids with variants; loops are set to
/// loop on load. Missing ids return null, so the game runs (silently) without the set.
/// </summary>
public static class SfxLibrary
{
    private const string Dir = "res://assets/audio/sfx/";
    private sealed record Entry(string[] Files, bool Loop);

    private static Dictionary<string, Entry>? _entries;
    private static readonly Dictionary<string, AudioStream?> Cache = new();
    private static readonly Random Rng = new(4242);

    private static Dictionary<string, Entry> Entries
    {
        get
        {
            if (_entries != null) return _entries;
            _entries = new Dictionary<string, Entry>();
            if (!FileAccess.FileExists(Dir + "manifest.json")) return _entries;
            using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(Dir + "manifest.json"));
            foreach (var sound in doc.RootElement.GetProperty("sounds").EnumerateObject())
            {
                var files = new List<string>();
                foreach (var f in sound.Value.GetProperty("files").EnumerateArray()) files.Add(f.GetString() ?? "");
                bool loop = sound.Value.TryGetProperty("loop", out var l) && l.GetBoolean();
                _entries[sound.Name] = new Entry(files.ToArray(), loop);
            }
            return _entries;
        }
    }

    public static bool Has(string id) => Entries.ContainsKey(id);
    public static int Count => Entries.Count;

    /// <summary>A variant of the sound (random when <paramref name="variant"/> is -1), or null if it doesn't exist.</summary>
    public static AudioStream? Get(string id, int variant = -1)
    {
        if (!Entries.TryGetValue(id, out var e) || e.Files.Length == 0) return null;
        string file = e.Files[variant < 0 ? Rng.Next(e.Files.Length) : variant % e.Files.Length];
        if (Cache.TryGetValue(file, out var cached)) return cached;
        AudioStream? stream = ResourceLoader.Exists(Dir + file) ? GD.Load<AudioStream>(Dir + file) : null;
        if (stream is AudioStreamWav wav && e.Loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
        }
        else if (stream is AudioStreamOggVorbis ogg) ogg.Loop = e.Loop;
        Cache[file] = stream;
        return stream;
    }

    /// <summary>Loads every sound once (at the start of an event, not at the first crash).</summary>
    public static void Preload()
    {
        foreach (var (id, e) in Entries)
            for (int i = 0; i < e.Files.Length; i++) Get(id, i);
    }
}

/// <summary>
/// A pool of one-shot voices: 3D voices for the world (impacts, glass, parts) and 2D voices for stings and UI.
/// When every voice is busy the oldest is stolen.
/// </summary>
public partial class SfxPool : Node
{
    private readonly List<AudioStreamPlayer3D> _voices3D = new();
    private readonly List<AudioStreamPlayer> _voices2D = new();
    private readonly List<ulong> _started3D = new();
    private int _next2D;

    public int Voices3D { get; set; } = 28;
    public int Voices2D { get; set; } = 8;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        for (int i = 0; i < Voices3D; i++)
        {
            var p = new AudioStreamPlayer3D
            {
                Bus = AudioMix.Sfx,
                UnitSize = 9f,
                MaxDistance = 220f,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                MaxPolyphony = 1,
                DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
            };
            AddChild(p);
            _voices3D.Add(p);
            _started3D.Add(0);
        }
        for (int i = 0; i < Voices2D; i++)
        {
            var p = new AudioStreamPlayer { Bus = AudioMix.Sfx };
            AddChild(p);
            _voices2D.Add(p);
        }
    }

    public void Play3D(string id, Vector3 at, float volumeDb = 0f, float pitch = 1f, string bus = AudioMix.Sfx)
    {
        var stream = SfxLibrary.Get(id);
        if (stream == null) return;
        int pick = -1;
        ulong oldest = ulong.MaxValue;
        for (int i = 0; i < _voices3D.Count; i++)
        {
            if (!_voices3D[i].Playing) { pick = i; break; }
            if (_started3D[i] < oldest) { oldest = _started3D[i]; pick = i; }
        }
        var v = _voices3D[pick];
        v.Stop();
        v.Stream = stream;
        v.Bus = bus;
        v.GlobalPosition = at;
        v.VolumeDb = volumeDb;
        v.PitchScale = pitch;
        v.Play();
        _started3D[pick] = Time.GetTicksMsec();
    }

    public void Play2D(string id, float volumeDb = 0f, float pitch = 1f, string bus = AudioMix.Sfx)
    {
        var stream = SfxLibrary.Get(id);
        if (stream == null) return;
        var v = _voices2D[_next2D];
        _next2D = (_next2D + 1) % _voices2D.Count;
        v.Stop();
        v.Stream = stream;
        v.Bus = bus;
        v.VolumeDb = volumeDb;
        v.PitchScale = pitch;
        v.Play();
    }
}
