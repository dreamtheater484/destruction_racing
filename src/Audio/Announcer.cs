using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// The stadium announcer (plan §12): lines by category from assets/audio/announcer/manifest.json (Kokoro TTS
/// through a stadium PA chain, tools/announcer), with anti-repetition — a line is not repeated within 3 minutes
/// while the category has others — a priority queue, a minimum gap between lines and a frequency setting.
/// Any line can be replaced by a recording with the same id in <c>user://announcer/</c> or an <c>Announcer</c>
/// folder next to the game (docs/announcer_recording_guide.md).
/// </summary>
public partial class Announcer : Node
{
    public enum Priority { Filler = 0, Normal = 1, High = 2, Critical = 3 }

    private const string Dir = "res://assets/audio/announcer/";
    private const double NoRepeatSeconds = 180.0;

    private sealed record Line(string Id, string Category, string File, float Duration);

    /// <summary>Debug (--audiolog): print every line as it is said.</summary>
    public bool Log { get; set; }
    public int LineCount => _byCategory.Where(kv => !kv.Key.StartsWith("#")).Sum(kv => kv.Value.Count);

    /// <summary>0 = silent, 1 = normal, 2 = chatty (an option, plan §12).</summary>
    public float Frequency { get; set; } = 1f;
    public bool Speaking => _player.Playing;

    private readonly Dictionary<string, List<Line>> _byCategory = new();
    private readonly Dictionary<string, double> _lastPlayed = new();
    private readonly Dictionary<string, AudioStream?> _streams = new();
    private readonly List<(string Category, Priority Priority, double Expires)> _queue = new();
    private readonly Random _rng = new(1977);
    private AudioStreamPlayer _player = null!;
    private Priority _playingPriority;
    private double _clock, _quietUntil;
    private string[] _overrideDirs = Array.Empty<string>();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _player = new AudioStreamPlayer { Bus = AudioMix.Announcer };
        AddChild(_player);
        _overrideDirs = new[]
        {
            ProjectSettings.GlobalizePath("user://announcer"),
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "Announcer"),
            ProjectSettings.GlobalizePath("res://Announcer"),
        };
        Load();
    }

    private void Load()
    {
        if (!FileAccess.FileExists(Dir + "manifest.json")) return;
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(Dir + "manifest.json"));
        foreach (var l in doc.RootElement.GetProperty("lines").EnumerateArray())
        {
            var line = new Line(l.GetProperty("id").GetString() ?? "", l.GetProperty("category").GetString() ?? "",
                l.GetProperty("file").GetString() ?? "", l.TryGetProperty("duration", out var d) ? d.GetSingle() : 2f);
            if (!_byCategory.TryGetValue(line.Category, out var list)) _byCategory[line.Category] = list = new List<Line>();
            list.Add(line);
            // Every line can also be asked for by its id ("#wrecked_num_23").
            _byCategory["#" + line.Id] = new List<Line> { line };
        }
    }

    public bool HasCategory(string category) => _byCategory.ContainsKey(category);
    public bool HasLine(string id) => _byCategory.ContainsKey("#" + id);

    /// <summary>Asks for one specific line by its id.</summary>
    public void SayLine(string id, Priority priority = Priority.Normal, double maxWait = 2.5) => Say("#" + id, priority, maxWait);

    /// <summary>
    /// Asks for a line from a category. Higher priorities cut lower ones off; otherwise it waits in the queue for
    /// up to <paramref name="maxWait"/> seconds and is dropped if it gets stale.
    /// </summary>
    public void Say(string category, Priority priority = Priority.Normal, double maxWait = 2.5)
    {
        if (Frequency <= 0f || !_byCategory.ContainsKey(category)) return;
        // At low frequency only the important calls get through; fillers are the first to go.
        if (priority == Priority.Filler && _rng.NextDouble() > Frequency * 0.6) return;
        if (_player.Playing && priority > _playingPriority && priority >= Priority.High)
        {
            _player.Stop();
            _queue.Clear();
        }
        _queue.RemoveAll(q => q.Category == category);
        _queue.Add((category, priority, _clock + maxWait));
    }

    public override void _Process(double delta)
    {
        double rdt = delta / Math.Max(Engine.TimeScale, 0.01);
        _clock += rdt;
        AudioMix.DuckForAnnouncer(_player.Playing ? 1f : 0f);
        _queue.RemoveAll(q => q.Expires < _clock);
        if (_player.Playing || _clock < _quietUntil || _queue.Count == 0) return;
        var next = _queue.OrderByDescending(q => q.Priority).ThenBy(q => q.Expires).First();
        _queue.Remove(next);
        var line = Pick(next.Category);
        if (line == null) return;
        var stream = StreamFor(line);
        if (stream == null) return;
        _lastPlayed[line.Id] = _clock;
        _player.Stream = stream;
        _player.Play();
        _playingPriority = next.Priority;
        if (Log) GD.Print($"[Announcer] {line.Id} ({next.Priority})");
        // A breath between lines; more at low frequency.
        _quietUntil = _clock + stream.GetLength() + 0.6 + (Frequency < 1f ? (1f - Frequency) * 4.0 : 0.0);
    }

    /// <summary>The least recently played line of the category; never one played in the last 3 minutes if another is free.</summary>
    private Line? Pick(string category)
    {
        if (!_byCategory.TryGetValue(category, out var lines) || lines.Count == 0) return null;
        var fresh = lines.Where(l => !_lastPlayed.TryGetValue(l.Id, out double t) || _clock - t > NoRepeatSeconds).ToList();
        if (fresh.Count > 0) return fresh[_rng.Next(fresh.Count)];
        return lines.OrderBy(l => _lastPlayed.GetValueOrDefault(l.Id, -1e9)).First();
    }

    private AudioStream? StreamFor(Line line)
    {
        if (_streams.TryGetValue(line.Id, out var cached)) return cached;
        AudioStream? stream = null;
        foreach (string dir in _overrideDirs)
        {
            foreach (string ext in new[] { ".ogg", ".wav", ".mp3" })
            {
                string path = System.IO.Path.Combine(dir, line.Id + ext);
                if (!System.IO.File.Exists(path)) continue;
                stream = ext switch
                {
                    ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
                    ".wav" => AudioStreamWav.LoadFromFile(path),
                    _ => new AudioStreamMP3 { Data = System.IO.File.ReadAllBytes(path) },
                };
                if (stream != null) break;
            }
            if (stream != null) break;
        }
        stream ??= ResourceLoader.Exists(Dir + line.File) ? GD.Load<AudioStream>(Dir + line.File) : null;
        _streams[line.Id] = stream;
        return stream;
    }
}
