using System;
using System.Collections.Generic;
using DestructionRacing.Core;
using DestructionRacing.Vehicles;

namespace DestructionRacing.AI;

/// <summary>
/// The PlayerProxy bot (plan §17.1): a derby brain decides, but its decisions go through the same path as a
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

    public PlayerProxyInput(DerbyBrain brain, IArenaView view, int seed)
    {
        Brain = brain;
        View = view;
        _rng = new Random(seed);
        _delay = 0.2f;
        _nextMistake = 6 + _rng.NextDouble() * 8;
        _filter.IsHeld = Held;
    }

    public DerbyBrain Brain { get; }
    public IArenaView View { get; }

    public CarControls GetControls(Car car, float delta)
    {
        _clock += delta;
        if (_clock >= _nextDelayChange)
        {
            _delay = 0.15f + (float)_rng.NextDouble() * 0.10f;
            _nextDelayChange = _clock + 0.5;
        }

        // What the brain wants now, as keys.
        var c = Brain.Think(car, View, delta);
        var wanted = new Keys(
            gas: c.Throttle >= 0.35f && !c.Reverse,   // a human presses gas for any real throttle request
            brake: c.Brake > 0.5f,
            reverse: c.Reverse,
            left: c.Steer < -0.3f,
            right: c.Steer > 0.3f,
            fast: c.FastSteer,
            turn: c.TurnAround);

        // An occasional mistake: a wrong steering input for a moment.
        if (_clock >= _nextMistake)
        {
            _mistakeUntil = _clock + 0.25 + _rng.NextDouble() * 0.2;
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
