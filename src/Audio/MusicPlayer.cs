using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// Music (plan §12): the game's own tracks (assets/audio/music, royalty-free, credited in CREDITS.md) plus the
/// player's own OGG/MP3 files from a <c>Music</c> folder next to the game or in <c>user://music</c>; shuffled.
/// <see cref="Intensity"/> (0..3) brings the music up as the derby escalates (5, 3, 2 cars left).
/// </summary>
public partial class MusicPlayer : Node
{
    private const string Dir = "res://assets/audio/music/";

    public int Intensity { get; set; }
    /// <summary>Faded out (results, replays) or playing.</summary>
    public bool Muted { get; set; }

    private AudioStreamPlayer _player = null!;
    private readonly List<Func<AudioStream?>> _tracks = new();
    private readonly Random _rng = new((int)(Time.GetUnixTimeFromSystem() % 100000));
    private int _next;
    private float _level = -40f;

    public int TrackCount => _tracks.Count;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _player = new AudioStreamPlayer { Bus = AudioMix.Music, VolumeDb = -40f };
        AddChild(_player);
        _player.Finished += PlayNext;
        Scan();
        // Shuffle once, then play through.
        for (int i = _tracks.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_tracks[i], _tracks[j]) = (_tracks[j], _tracks[i]);
        }
        PlayNext();
    }

    private void Scan()
    {
        if (DirAccess.Open(Dir) is { } dir)
            foreach (string f in dir.GetFiles())
            {
                string name = f.EndsWith(".import") ? f[..^7] : f;
                if (!(name.EndsWith(".ogg") || name.EndsWith(".mp3"))) continue;
                string path = Dir + name;
                if (_tracks.Count < 64 && !_known.Contains(path))
                {
                    _known.Add(path);
                    _tracks.Add(() => ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null);
                }
            }
        string exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        foreach (string folder in new[] { System.IO.Path.Combine(exeDir, "Music"), ProjectSettings.GlobalizePath("user://music"), ProjectSettings.GlobalizePath("res://Music") })
        {
            if (!System.IO.Directory.Exists(folder)) continue;
            foreach (string file in System.IO.Directory.GetFiles(folder).OrderBy(f => f))
            {
                string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".ogg") _tracks.Add(() => AudioStreamOggVorbis.LoadFromFile(file));
                else if (ext == ".mp3") _tracks.Add(() => new AudioStreamMP3 { Data = System.IO.File.ReadAllBytes(file) });
            }
        }
    }

    private readonly HashSet<string> _known = new();

    private void PlayNext()
    {
        for (int tries = 0; tries < _tracks.Count; tries++)
        {
            var stream = _tracks[_next % _tracks.Count]();
            _next++;
            if (stream == null) continue;
            if (stream is AudioStreamOggVorbis ogg) ogg.Loop = false;
            if (stream is AudioStreamMP3 mp3) mp3.Loop = false;
            _player.Stream = stream;
            _player.Play();
            return;
        }
    }

    public override void _Process(double delta)
    {
        float rdt = (float)(delta / Math.Max(Engine.TimeScale, 0.01));
        float target = Muted || _tracks.Count == 0 ? -40f : -6f + 2f * Math.Clamp(Intensity, 0, 3);
        _level = Mathf.MoveToward(_level, target, rdt * (target > _level ? 10f : 20f));
        _player.VolumeDb = _level;
    }
}
