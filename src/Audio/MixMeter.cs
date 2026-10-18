using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// The mix meter (<c>--mixlog</c>, plan §12 "final mix"): captures every bus after its fader and prints, every
/// 30 s and when the event closes, each bus's loudness: integrated over the event (400 ms blocks, gated as in
/// ITU BS.1770: below −70 dB and more than 10 dB under the mean they don't count; plain RMS, no K-weighting, so
/// read it as roughly LUFS), the loudest 3 s, and the sample peak. Run it in real time (no <c>--fixed-fps</c>).
/// </summary>
public partial class MixMeter : Node
{
    private static readonly string[] Buses = { "Master", AudioMix.Music, AudioMix.Announcer, AudioMix.Sfx, AudioMix.Engines, AudioMix.Crowd };

    private sealed class Meter
    {
        public required string Bus;
        public required AudioEffectCapture Capture;
        public double BlockSum;
        public int BlockFrames;
        public readonly List<double> Blocks = new();     // mean square per 400 ms block
        public float Peak;
    }

    private readonly List<Meter> _meters = new();
    private const int BlockFrames = 19200;               // 400 ms at 48 kHz (the mix rate is read at start)
    private int _block = BlockFrames;
    private double _sinceReport;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _block = (int)(AudioServer.GetMixRate() * 0.4f);
        foreach (string bus in Buses)
        {
            int i = AudioServer.GetBusIndex(bus);
            if (i < 0) continue;
            var cap = new AudioEffectCapture { BufferLength = 1f };
            AudioServer.AddBusEffect(i, cap);
            _meters.Add(new Meter { Bus = bus, Capture = cap });
        }
    }

    public override void _ExitTree()
    {
        Report("final");
        foreach (var m in _meters)
        {
            int i = AudioServer.GetBusIndex(m.Bus);
            if (i < 0) continue;
            for (int e = AudioServer.GetBusEffectCount(i) - 1; e >= 0; e--)
                if (AudioServer.GetBusEffect(i, e) == m.Capture) AudioServer.RemoveBusEffect(i, e);
        }
    }

    public override void _Process(double delta)
    {
        foreach (var m in _meters)
        {
            int frames = m.Capture.GetFramesAvailable();
            if (frames <= 0) continue;
            var buf = m.Capture.GetBuffer(frames);
            // The capture is before the fader: the bus's own volume (and the World bus's, for its children) on top.
            float gain = Mathf.DbToLinear(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(m.Bus)));
            if (m.Bus is AudioMix.Sfx or AudioMix.Engines or AudioMix.Crowd)
                gain *= Mathf.DbToLinear(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(AudioMix.World)));
            foreach (var s in buf)
            {
                float l = s.X * gain, r = s.Y * gain;
                m.Peak = Math.Max(m.Peak, Math.Max(Math.Abs(l), Math.Abs(r)));
                m.BlockSum += (l * l + r * r) * 0.5;
                if (++m.BlockFrames >= _block)
                {
                    m.Blocks.Add(m.BlockSum / m.BlockFrames);
                    m.BlockSum = 0;
                    m.BlockFrames = 0;
                }
            }
        }
        _sinceReport += delta / Math.Max(Engine.TimeScale, 0.01);
        if (_sinceReport >= 30.0)
        {
            _sinceReport = 0;
            Report("so far");
        }
    }

    private static double Db(double meanSquare) => meanSquare <= 1e-12 ? -120 : 10 * Math.Log10(meanSquare);

    private void Report(string when)
    {
        var parts = new List<string>();
        foreach (var m in _meters)
        {
            var blocks = m.Blocks.Where(b => Db(b) > -70).ToList();
            double integrated = -120, shortMax = -120;
            if (blocks.Count > 0)
            {
                double gate = Db(blocks.Average()) - 10;
                var gated = blocks.Where(b => Db(b) > gate).ToList();
                integrated = Db(gated.Average());
            }
            // The loudest 3 s (7.5 blocks: windows of 8).
            for (int i = 0; i + 8 <= m.Blocks.Count; i++) shortMax = Math.Max(shortMax, Db(m.Blocks.Skip(i).Take(8).Average()));
            parts.Add($"{m.Bus} {integrated:0.0}/{shortMax:0.0}/{20 * Math.Log10(Math.Max(m.Peak, 1e-6f)):0.0}");
        }
        GD.Print($"[Mix] {when} ({_meters.FirstOrDefault()?.Blocks.Count * 0.4:0} s)  integrated/loudest 3 s/peak dB:  " + string.Join("   ", parts));
    }
}
