using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Modes;
using DestructionRacing.Rules;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// Audio v1 of the derby (plan §12): layered crashes by hit energy and surface, glass, lamps and parts, every
/// car's continuous loops (<see cref="CarAudio"/>), engines (granular voices on the six cars nearest the camera),
/// the crowd bed and its reactions, stings for spins, wrecks and banners, the countdown, the mix (ducking,
/// slow-motion muffle), music intensity and the announcer (<see cref="DerbyCommentary"/>).
/// </summary>
public partial class DerbyAudio : Node
{
    public DerbyMode Mode { get; set; } = null!;

    public SfxPool Pool { get; private set; } = null!;
    public Announcer Announcer { get; private set; } = null!;
    public MusicPlayer Music { get; private set; } = null!;

    private const int EngineVoices = 6;
    private readonly Dictionary<Car, CarAudio> _cars = new();
    private readonly GranularEngine?[] _engines = new GranularEngine?[EngineVoices];
    private readonly Car?[] _engineCar = new Car?[EngineVoices];
    private float _reassign;
    private AudioStreamPlayer _crowdBed = null!, _crowdHot = null!;
    private double _lastOoh = -10, _lastCheer = -10;
    private readonly HashSet<(DamageZone, int)> _warned = new();
    private float _slowMo, _slowMoTarget;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        AudioMix.Ensure();
        SfxLibrary.Preload();
        Pool = new SfxPool();
        AddChild(Pool);
        Announcer = new Announcer();
        AddChild(Announcer);
        Music = new MusicPlayer();
        AddChild(Music);

        int seed = 1;
        foreach (var car in Mode.Cars)
        {
            var audio = new CarAudio(seed++) { Car = car, Pool = Pool, IsPlayer = car == Mode.PlayerCar };
            AddChild(audio);
            _cars[car] = audio;
        }
        for (int i = 0; i < EngineVoices; i++)
        {
            // One voice per slot; the engine type is switched when the slot moves to another model.
            if (!FileAccess.FileExists("res://assets/audio/engines/kitsune.json")) break;
            _engines[i] = new GranularEngine { EngineName = "kitsune", Bus = AudioMix.Engines, UnitSize = 7f, MaxDistance = 120f, VolumeDb = -80f };
            AddChild(_engines[i]);
        }

        _crowdBed = new AudioStreamPlayer { Stream = SfxLibrary.Get("crowd_bed_loop"), Bus = AudioMix.Crowd, VolumeDb = -80f };
        _crowdHot = new AudioStreamPlayer { Stream = SfxLibrary.Get("crowd_excited_loop"), Bus = AudioMix.Crowd, VolumeDb = -80f };
        AddChild(_crowdBed);
        AddChild(_crowdHot);
        if (_crowdBed.Stream != null) _crowdBed.Play();
        if (_crowdHot.Stream != null) _crowdHot.Play();

        Mode.ImpactHappened += OnImpact;
        Mode.RefereeEventRaised += OnRefereeEvent;
        Mode.CountdownTick += OnCountdown;
        Mode.Started += OnStarted;
        Mode.RemainingChanged += OnRemaining;
        Mode.Finished += OnFinished;
        CarVisual.Moment += OnMoment;
        if (Mode.Presenter != null) Mode.Presenter.SlowMoChanged += OnSlowMo;
        if (Mode.Hud != null)
        {
            Mode.Hud.BannerShown += () => Pool.Play2D("banner_whoosh", -8f);
            Mode.Hud.TallyTicked += () => Pool.Play2D("tally_tick", -12f, 0.95f + GD.Randf() * 0.1f);
            Mode.Hud.TallyFinished += () => Pool.Play2D("tally_done", -8f);
        }
        AddChild(new DerbyCommentary { Mode = Mode, Announcer = Announcer });
        Announcer.Log = Core.DevTools.Instance?.Arg("audiolog") != null;
        if (Announcer.Log)
            GD.Print($"[Audio] {SfxLibrary.Count} sound ids, {Announcer.LineCount} announcer lines, {Music.TrackCount} music tracks, " +
                     $"{_engines.Count(e => e != null)} engine voices, buses {AudioServer.BusCount}");
    }

    public override void _ExitTree()
    {
        Mode.ImpactHappened -= OnImpact;
        Mode.RefereeEventRaised -= OnRefereeEvent;
        Mode.CountdownTick -= OnCountdown;
        Mode.Started -= OnStarted;
        Mode.RemainingChanged -= OnRemaining;
        Mode.Finished -= OnFinished;
        CarVisual.Moment -= OnMoment;
        AudioMix.SlowMo(0f);
        AudioMix.DuckForAnnouncer(0f);
    }

    // ------------------------------------------------------------------ events

    private static float Jitter(float range) => 1f + (GD.Randf() * 2f - 1f) * range;

    private void OnImpact(Impact i) =>
        ImpactSound(i.Point, i.Energy, i.B == null, i.A == Mode.PlayerCar || i.B == Mode.PlayerCar);

    /// <summary>The sound of a recorded hit during a replay.</summary>
    public void PlayReplayImpact(Replay.ReplayImpact hit, Car? a, Car? b) =>
        ImpactSound(hit.Point, hit.Energy, hit.B < 0, a == Mode.PlayerCar || b == Mode.PlayerCar);

    private void ImpactSound(Vector3 point, float energy, bool wall, bool involvesPlayer)
    {
        if (energy < 4_000f) return;
        var i = (Point: point, Energy: energy);
        string surface = wall ? "tyres" : "metal";
        string tier;
        float t;   // 0..1 within the tier, for the level
        if (i.Energy < 20_000f) { tier = "light"; t = (i.Energy - 4_000f) / 16_000f; }
        else if (i.Energy < 50_000f) { tier = "medium"; t = (i.Energy - 20_000f) / 30_000f; }
        else if (i.Energy < 110_000f) { tier = "heavy"; t = (i.Energy - 50_000f) / 60_000f; }
        else { tier = wall ? "heavy" : "huge"; t = Mathf.Clamp((i.Energy - 110_000f) / 120_000f, 0f, 1f); }
        float db = -6f + 6f * t + (involvesPlayer ? 2f : 0f);
        Pool.Play3D($"hit_{surface}_{tier}", i.Point, db, Jitter(0.07f));
        // Layers for the big ones: the body crunch on a wall, debris rattling after a huge hit.
        if (wall && i.Energy >= 60_000f) Pool.Play3D("hit_concrete_heavy", i.Point, db - 4f, Jitter(0.05f));
        if (i.Energy >= 110_000f) Pool.Play3D("debris_clatter", i.Point + Vector3.Up * 0.3f, -6f, Jitter(0.1f));
        if (i.Energy >= 50_000f) AudioMix.DuckForCrash(Mathf.Clamp(i.Energy / 150_000f, 0.3f, 1f));
        // The crowd: an "oooh" for the big ones.
        if (i.Energy >= 70_000f && Mode.Time - _lastOoh > 3.0)
        {
            _lastOoh = Mode.Time;
            Pool.Play2D("crowd_ooh", -4f + 4f * Mathf.Clamp(i.Energy / 200_000f, 0f, 1f), Jitter(0.05f), AudioMix.Crowd);
        }
    }

    private void OnMoment(CarVisual visual, string what, Vector3 at)
    {
        if (!_cars.ContainsKey(visual.Car)) return;
        switch (what)
        {
            case "glass": Pool.Play3D("glass_break", at, -3f, Jitter(0.08f)); break;
            case "lamp" or "headlamp": Pool.Play3D("lamp_break", at, -5f, Jitter(0.1f)); break;
            case "wheel":
                Pool.Play3D("part_off", at, -4f, Jitter(0.08f));
                GetTree().CreateTimer(0.45).Timeout += () => Pool.Play3D("wheel_bounce", at, -3f, Jitter(0.1f));
                break;
            case "roll": Pool.Play3D("hit_metal_heavy", at, -2f, 0.85f); break;
            default: Pool.Play3D("part_off", at, -4f, Jitter(0.08f)); break;
        }
    }

    private void OnRefereeEvent(RefereeEvent e)
    {
        int player = Mode.PlayerCar.Id;
        switch (e.Kind)
        {
            case RefereeEventKind.SpinTier when e.Other == player:
                Pool.Play2D(e.Tier switch { SpinTier.Quarter => "sting_spin_1", SpinTier.Half => "sting_spin_2", _ => "sting_spin_4" }, -9f);
                break;
            case RefereeEventKind.SpinScored when e.Car == player && Mode.Time - _lastCheer > 2.0:
                _lastCheer = Mode.Time;
                Pool.Play2D("crowd_cheer", -6f, Jitter(0.05f), AudioMix.Crowd);
                break;
            case RefereeEventKind.Wrecked:
                if (e.Other == player) Pool.Play2D("sting_wreck", -7f);
                Pool.Play2D(e.Car == player ? "crowd_groan" : "crowd_roar", e.Car == player || e.Other == player ? -2f : -8f, Jitter(0.05f), AudioMix.Crowd);
                break;
            case RefereeEventKind.Damage when e.Car == player:
            {
                // Dashboard warning beeps as a zone goes critical.
                float h = Mode.PlayerCar.Health(e.Zone);
                foreach (int level in new[] { 25, 10 })
                    if (h * 100f < level && _warned.Add((e.Zone, level))) Pool.Play2D("warning_beep", -12f);
                break;
            }
        }
    }

    private void OnCountdown(int n) => Pool.Play2D("countdown_beep", -9f);

    private void OnStarted()
    {
        Pool.Play2D("countdown_go", -7f);
        Pool.Play2D("air_horn", -8f);
        Pool.Play2D("crowd_cheer", -2f, 1f, AudioMix.Crowd);
    }

    private void OnRemaining(int remaining)
    {
        Music.Intensity = remaining switch { <= 2 => 3, <= 3 => 2, <= 5 => 1, _ => 0 };
        if (remaining == 2) Pool.Play2D("sting_final", -6f);
    }

    private void OnFinished(DerbyMode mode)
    {
        Pool.Play2D("crowd_roar", 0f, 1f, AudioMix.Crowd);
        GetTree().CreateTimer(2.5, true, false, true).Timeout += () => Pool.Play2D("crowd_applause", -3f, 1f, AudioMix.Crowd);
        Music.Intensity = 1;
    }

    private void OnSlowMo(bool on)
    {
        _slowMoTarget = on ? 1f : 0f;
        if (on) Pool.Play2D("slowmo_in", -8f);
    }

    // ------------------------------------------------------------------ per frame

    public override void _Process(double delta)
    {
        float rdt = (float)(delta / Math.Max(Engine.TimeScale, 0.01));
        _slowMo = Mathf.MoveToward(_slowMo, _slowMoTarget, rdt * 5f);
        AudioMix.SlowMo(_slowMo);
        AudioMix.Update(rdt);

        // The crowd bed swells into cheering with the excitement.
        float ex = Mode.Arena.Crowd?.Excitement ?? 0.3f;
        _crowdBed.VolumeDb = Mathf.Lerp(-4f, -12f, ex);
        _crowdHot.VolumeDb = ex < 0.15f ? -60f : Mathf.Lerp(-30f, -2f, Mathf.Clamp((ex - 0.15f) / 0.75f, 0f, 1f));

        UpdateEngines(rdt);
    }

    private void UpdateEngines(float rdt)
    {
        if (_engines[0] == null) return;
        _reassign -= rdt;
        if (_reassign <= 0f)
        {
            _reassign = 0.3f;
            var cam = GetViewport().GetCamera3D();
            var from = cam?.GlobalPosition ?? Vector3.Zero;
            var focus = Mode.Rig?.Target;
            var want = Mode.Cars.Where(c => !c.IsWrecked)
                .OrderBy(c => c == focus || c == Mode.PlayerCar && !Mode.PlayerOut ? -1f : c.GlobalPosition.DistanceSquaredTo(from))
                .Take(EngineVoices).ToList();
            var free = new Queue<int>();
            for (int i = 0; i < EngineVoices; i++)
            {
                if (_engineCar[i] is { } c && want.Remove(c)) continue;
                _engineCar[i] = null;
                free.Enqueue(i);
            }
            foreach (var c in want)
            {
                if (free.Count == 0) break;
                int slot = free.Dequeue();
                _engineCar[slot] = c;
                var engine = _engines[slot]!;
                string model = string.IsNullOrEmpty(c.ModelId) ? "kitsune" : c.ModelId;
                if (engine.EngineName != model && FileAccess.FileExists($"res://assets/audio/engines/{model}.json")) engine.SwitchEngine(model);
                if (engine.Bank != null && _cars.TryGetValue(c, out var ca))
                {
                    ca.IdleRpm = engine.Bank.IdleRpm;
                    ca.RedlineRpm = engine.Bank.RedlineRpm;
                }
            }
        }
        for (int i = 0; i < EngineVoices; i++)
        {
            var engine = _engines[i]!;
            var car = _engineCar[i];
            if (car == null || !_cars.TryGetValue(car, out var ca) || car.IsWrecked)
            {
                engine.VolumeDb = Mathf.MoveToward(engine.VolumeDb, -80f, rdt * 120f);
                continue;
            }
            engine.GlobalPosition = car.GlobalPosition;
            engine.Rpm = Mathf.Max(ca.Rpm, 300f);
            engine.Load = ca.Load;
            float target = (car == Mode.PlayerCar ? 0f : -3f) + (ca.Load > 0.5f ? 0f : -3f);
            engine.VolumeDb = Mathf.MoveToward(engine.VolumeDb, target, rdt * 60f);
        }
    }
}
