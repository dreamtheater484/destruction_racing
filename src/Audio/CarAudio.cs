using System;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// One car's continuous sounds (plan §12): metal or wall scrape, tyres on the dirt floor (with squeal when it
/// slides), a bare hub grinding, fire and steam. The loops only run near the listener. Also keeps the car's
/// engine state (a simple gearbox: rpm and load for the granular engine voice) and its misfires.
/// </summary>
public partial class CarAudio : Node3D
{
    public Car Car { get; set; } = null!;
    public SfxPool? Pool { get; set; }
    /// <summary>The player's car: a little louder, plus misfires on a battered front.</summary>
    public bool IsPlayer { get; set; }

    private const float ActiveRange = 48f;
    private AudioStreamPlayer3D _scrape = null!, _tyres = null!, _squeal = null!, _grind = null!, _fire = null!, _steam = null!;
    private bool _scrapeWall;
    private readonly Random _rng;
    private float _misfireIn = 2f;

    // Engine state for the granular voice.
    public float Rpm { get; private set; }
    public float Load { get; private set; }
    public float IdleRpm { get; set; } = 850f;
    public float RedlineRpm { get; set; } = 6200f;
    private int _gear = 1;
    private static readonly float[] UpShift = { 0f, 9.5f, 17.5f, 26.5f, 999f };
    private static readonly float[] DownShift = { 0f, 0f, 7.5f, 14.5f, 22.5f };

    public CarAudio(int seed) { _rng = new Random(seed); }
    public CarAudio() : this(1) { }

    public override void _Ready()
    {
        _scrape = Loop("scrape_metal_loop", 6f);
        _tyres = Loop("tyre_dirt_loop", 6f);
        _squeal = Loop("tyre_squeal_loop", 7f);
        _grind = Loop("hub_grind_loop", 6f);
        _fire = Loop("fire_loop", 7f);
        _steam = Loop("steam_loop", 4f);
        Rpm = IdleRpm;
    }

    private AudioStreamPlayer3D Loop(string id, float unitSize)
    {
        var p = new AudioStreamPlayer3D
        {
            Stream = SfxLibrary.Get(id),
            Bus = AudioMix.Sfx,
            UnitSize = unitSize,
            MaxDistance = ActiveRange + 10f,
            VolumeDb = -80f,
            DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
        };
        AddChild(p);
        return p;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!IsInstanceValid(Car)) return;
        GlobalPosition = Car.GlobalPosition;
        UpdateEngine(dt);

        var listener = GetViewport().GetCamera3D();
        bool near = listener != null && listener.GlobalPosition.DistanceTo(Car.GlobalPosition) < ActiveRange;
        var visual = Car.Visual;
        float speed = Car.LinearVelocity.Length();
        bool grounded = Car.WheelsGrounded >= 2;
        bool wrecked = Car.IsWrecked;

        float scrape = visual?.ScrapeLevel ?? 0f;
        if (visual != null && scrape > 0.05f && visual.ScrapeOnWall != _scrapeWall)
        {
            _scrapeWall = visual.ScrapeOnWall;
            _scrape.Stream = SfxLibrary.Get(_scrapeWall ? "scrape_wall_loop" : "scrape_metal_loop");
        }
        // Tyres: rolling on the dirt, sliding (body slip), spinning up from a standstill, the turn-around.
        float slip = Mathf.Clamp((Mathf.Abs(Car.BodySlipDeg) - 6f) / 30f, 0f, 1f) * Mathf.Clamp(speed / 8f, 0f, 1f);
        var c = Car.LastControls;
        if (!wrecked && c.Throttle > 0.8f && speed < 6f && grounded) slip = Mathf.Max(slip, 0.55f);
        if (!wrecked && c.TurnAround && speed < 9f) slip = Mathf.Max(slip, 0.8f);
        float roll = grounded ? Mathf.Clamp(speed / 22f, 0f, 1f) : 0f;
        int lost = Car.Damage?.WheelsLost ?? 0;
        float grind = lost > 0 && grounded ? Mathf.Clamp(speed / 10f, 0.15f, 1f) : 0f;
        float fire = visual?.Burning == true ? 1f : 0f;
        float front = Car.Health(DamageZone.Front);
        float steam = !wrecked && front < 0.6f && fire == 0f ? Mathf.Clamp((0.6f - front) / 0.4f, 0.2f, 1f) : 0f;

        float boost = IsPlayer ? 2f : 0f;
        Drive(_scrape, near, scrape, -4f + boost, 0.9f + 0.25f * scrape, dt);
        Drive(_tyres, near, Mathf.Max(roll * 0.45f, slip), -10f + boost, 0.85f + 0.3f * roll, dt);
        Drive(_squeal, near, slip * 0.6f, -12f + boost, 0.95f + 0.1f * slip, dt);
        Drive(_grind, near, grind, -4f + boost, 0.8f + 0.4f * grind, dt);
        Drive(_fire, near, fire, -6f, 1f, dt);
        Drive(_steam, near, steam, -14f, 1f, dt);

        // Misfires (plan §11 "own critical damage"): a battered engine coughs.
        if (IsPlayer && !wrecked && front < 0.35f && Pool != null)
        {
            _misfireIn -= dt;
            if (_misfireIn <= 0f)
            {
                _misfireIn = 0.4f + (float)_rng.NextDouble() * (0.6f + 3f * front);
                Pool.Play3D("misfire", Car.GlobalPosition + Car.GlobalBasis.Z * 2f, -4f, 0.9f + (float)_rng.NextDouble() * 0.2f);
            }
        }
    }

    /// <summary>Fades a loop towards a level (0..1); stops it when silent or out of range.</summary>
    private static void Drive(AudioStreamPlayer3D p, bool near, float level, float maxDb, float pitch, float dt)
    {
        if (p.Stream == null) return;
        float target = near && level > 0.02f ? maxDb + 20f * Mathf.Log(Mathf.Max(level, 0.01f)) / Mathf.Log(10f) : -80f;
        p.VolumeDb = Mathf.MoveToward(p.VolumeDb, target, dt * (target > p.VolumeDb ? 160f : 60f));
        p.PitchScale = Mathf.Lerp(p.PitchScale, pitch, 1f - Mathf.Exp(-8f * dt));
        bool audible = p.VolumeDb > -60f;
        if (audible && !p.Playing) p.Play((float)(GD.Randf() * Math.Max(0.1, p.Stream.GetLength() - 0.1)));
        else if (!audible && p.Playing) p.Stop();
    }

    /// <summary>A four-speed box from road speed; revs climb with throttle at a standstill (wheelspin).</summary>
    private void UpdateEngine(float dt)
    {
        if (Car.IsWrecked)
        {
            Rpm = Mathf.MoveToward(Rpm, 0f, dt * 3000f);
            Load = 0f;
            return;
        }
        var c = Car.LastControls;
        float speed = Mathf.Abs(Car.ForwardSpeed);
        float span = RedlineRpm - IdleRpm;
        float throttle = c.Reverse ? 1f : c.Throttle;
        float target;
        if (c.Reverse || Car.ForwardSpeed < -0.5f)
        {
            _gear = 1;
            target = IdleRpm + span * Mathf.Clamp(speed / 9f, 0f, 1f) * 0.85f;
        }
        else
        {
            if (speed > UpShift[_gear] && _gear < 4) _gear++;
            else if (_gear > 1 && speed < DownShift[_gear]) _gear--;
            float lo = _gear == 1 ? 0f : DownShift[_gear] - 2f;
            float hi = UpShift[_gear] < 900f ? UpShift[_gear] : 42f;
            target = IdleRpm + span * (0.12f + 0.83f * Mathf.Clamp((speed - lo) / (hi - lo), 0f, 1f));
        }
        // Wheelspin or airborne: the revs run away with the throttle.
        if (throttle > 0.5f && (Car.WheelsGrounded < 2 || speed < 4f)) target = Mathf.Max(target, IdleRpm + span * 0.55f * throttle);
        if (throttle < 0.1f && speed < 2f) target = IdleRpm;
        // A wrecked front makes the engine rough.
        float front = Car.Health(DamageZone.Front);
        if (front < 0.3f) target *= 1f + 0.06f * Mathf.Sin(Time.GetTicksMsec() * 0.031f) * (0.3f - front) / 0.3f;
        Rpm = Mathf.Lerp(Rpm, target, 1f - Mathf.Exp(-(target > Rpm ? 9f : 6f) * dt));
        Load = Mathf.Lerp(Load, throttle > 0.3f ? 1f : 0f, 1f - Mathf.Exp(-10f * dt));
    }
}
