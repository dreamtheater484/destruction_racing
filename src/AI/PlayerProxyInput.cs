using System;
using System.Collections.Generic;
using DestructionRacing.Core;
using DestructionRacing.Vehicles;

namespace DestructionRacing.AI;

/// <summary>
/// The PlayerProxy bot (plan §17.1): an AI brain (derby or race) decides, but its decisions go through the same path as a
/// human's — digital keys, a 150–250 ms reaction delay, occasional mistakes, then the keyboard steering filter.
/// Used by the sim harness to measure the player's experience.
/// </summary>
public sealed class PlayerProxyInput : ICarInput
{
    private readonly struct Keys
    {
        public readonly bool Gas, Brake, Reverse, Left, Right, Fast, Turn;
        public Keys(bool gas, bool brake, bool reverse, bool left, bool right, bool fast, bool turn)
        {
            Gas = gas; Brake = brake; Reverse = reverse; Left = left; Right = right; Fast = fast; Turn = turn;
        }
    }

    private readonly Queue<(double Time, Keys Keys)> _pending = new();
    private readonly PlayerInput _filter = new();
    private readonly Random _rng;
    private Keys _applied;
    private double _clock;
    private float _delay;
    private double _nextDelayChange;
    private double _mistakeUntil;
    private double _nextMistake;
    private bool _mistakeLeft;
    private float _plannedSteer;
    private int _tapDir;
    private double _tapChangedAt;
    /// <summary>The shortest key tap or release a player manages (about 10 taps a second at most).</summary>
    private const double MinTap = 0.05;

    private readonly Func<Car, float, CarControls> _think;

    public PlayerProxyInput(DerbyBrain brain, IArenaView view, int seed) : this((car, dt) => brain.Think(car, view, dt), seed) { }

    /// <summary>Races: a human taps the steering keys to hold a line at speed, so the race proxy does too.</summary>
    public PlayerProxyInput(RaceBrain brain, IRaceView view, int seed) : this((car, dt) => brain.Think(car, view, dt), seed)
    {
        TapSteering = true;
    }

    /// <summary>
    /// Steering by taps, as a player holds a line through a long bend: the proxy knows how the keyboard filter
    /// turns key presses into steering (ramp in, faster return) and taps so the steering follows what the brain
    /// asks for, with taps no shorter than 50 ms. Off for the derby proxy (its M1b calibration used held keys).
    /// </summary>
    public bool TapSteering { get; set; }

    public PlayerProxyInput(Func<Car, float, CarControls> think, int seed)
    {
        _think = think;
        _rng = new Random(seed);
        _delay = 0.2f;
        _nextMistake = 6 + _rng.NextDouble() * 8;
        _filter.IsHeld = Held;
    }

    public CarControls GetControls(Car car, float delta)
    {
        _clock += delta;
        if (_clock >= _nextDelayChange)
        {
            _delay = 0.15f + (float)_rng.NextDouble() * 0.10f;
            _nextDelayChange = _clock + 0.5;
        }

        // What the brain wants now, as keys.
        var c = _think(car, delta);
        bool left = c.Steer < -0.3f, right = c.Steer > 0.3f;
        if (TapSteering)
        {
            float target = c.Steer;
            int want = target > 0.03f ? (_plannedSteer < target ? 1 : 0)
                     : target < -0.03f ? (_plannedSteer > target ? -1 : 0)
                     : 0;
            if (MathF.Abs(target) > 0.95f) want = MathF.Sign(target) > 0 ? 1 : -1;
            if (want != _tapDir && _clock - _tapChangedAt >= MinTap)
            {
                _tapDir = want;
                _tapChangedAt = _clock;
            }
            // The same filter the keys will go through (PlayerInput), run on the planned keys.
            float rate = _tapDir == 0 ? _filter.SteerReturn
                : _plannedSteer != 0f && MathF.Sign(_tapDir) != MathF.Sign(_plannedSteer) ? _filter.SteerCounter
                : _filter.SteerRampIn;
            if (c.FastSteer) rate *= _filter.FastSteerRateScale;
            _plannedSteer = Godot.Mathf.MoveToward(_plannedSteer, _tapDir, rate * delta);
            left = _tapDir < 0;
            right = _tapDir > 0;
        }
        var wanted = new Keys(
            gas: c.Throttle >= 0.35f && !c.Reverse,   // a human presses gas for any real throttle request
            brake: c.Brake > 0.5f,
            reverse: c.Reverse,
            left: left,
            right: right,
            fast: c.FastSteer,
            turn: c.TurnAround);

        // An occasional mistake: a wrong steering input for a moment.
        if (_clock >= _nextMistake)
        {
            double length = 0.25 + _rng.NextDouble() * 0.2;
            // In a race a mistake at speed is a twitch, not a held wrong turn (that would crash every lap).
            if (TapSteering) length *= Math.Clamp(12.0 / Math.Max(car.ForwardSpeed, 12f), 0.25, 1.0);
            _mistakeUntil = _clock + length;
            _mistakeLeft = _rng.Next(2) == 0;
            _nextMistake = _clock + 6 + _rng.NextDouble() * 10;
        }
        if (_clock < _mistakeUntil)
            wanted = new Keys(wanted.Gas, wanted.Brake, wanted.Reverse, _mistakeLeft, !_mistakeLeft, false, false);

        // Human reaction time: the hands act on what the eyes saw a moment ago.
        _pending.Enqueue((_clock, wanted));
        while (_pending.Count > 0 && _pending.Peek().Time <= _clock - _delay) _applied = _pending.Dequeue().Keys;

        return _filter.GetControls(car, delta);
    }

    private bool Held(string action) => action switch
    {
        Controls.Accelerate => _applied.Gas,
        Controls.Brake => _applied.Brake,
        Controls.Reverse => _applied.Reverse,
        Controls.SteerLeft => _applied.Left,
        Controls.SteerRight => _applied.Right,
        Controls.FastSteer => _applied.Fast,
        Controls.TurnAround => _applied.Turn,
        _ => false,
    };
}
