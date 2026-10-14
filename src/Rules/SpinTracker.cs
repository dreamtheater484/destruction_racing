using System;
using System.Collections.Generic;

namespace DestructionRacing.Rules;

public enum SpinEventKind
{
    /// <summary>The victim passed a new tier while still rotating (for the live popup).</summary>
    TierReached,
    /// <summary>The spin window closed; the attacker is awarded the tier.</summary>
    Awarded,
}

public readonly record struct SpinEvent(SpinEventKind Kind, int AttackerId, SpinTier Tier);

/// <summary>
/// Tracks spins of one victim car and attributes them to the attacker (plan §5.3).
/// Feed it hits and per-physics-step yaw rates; it reports live tier upgrades and the final award.
/// Wall spins and self-spins never open a window, so they score nothing.
/// </summary>
public sealed class SpinTracker
{
    private readonly SpinTuning _t;

    private bool _open;
    private bool _rotating;
    private int _attacker;
    private double _openTime;
    private double _lastHitTime;
    private double _lastRotatingTime;
    private float _accumulatedDeg;
    private SpinTier _reported;

    public SpinTracker(SpinTuning tuning)
    {
        _t = tuning;
    }

    public bool IsOpen => _open;
    public int CurrentAttacker => _attacker;
    public float AccumulatedDegrees => _accumulatedDeg;

    /// <summary>Registers a car-car hit on this victim.</summary>
    public void RegisterHit(int attackerId, double time, float hitEnergy, float victimSpeed, bool victimWrecked)
    {
        if (victimWrecked || hitEnergy < _t.HitMinEnergy) return;

        if (!_open)
        {
            if (victimSpeed < _t.MinVictimSpeed) return;
            _open = true;
            _rotating = false;
            _attacker = attackerId;
            _openTime = time;
            _lastHitTime = time;
            _accumulatedDeg = 0f;
            _reported = SpinTier.None;
            return;
        }

        if (!_rotating)
        {
            // Not spinning yet: the most recent attacker gets the credit.
            _attacker = attackerId;
            _lastHitTime = time;
        }
        else if (hitEnergy >= _t.StealMinEnergy && attackerId != _attacker)
        {
            // A big new hit during a spin takes over attribution.
            _attacker = attackerId;
            _lastHitTime = time;
        }
    }

    /// <summary>Advances the tracker by one physics step. Events are appended to <paramref name="output"/>.</summary>
    public void Step(double time, float dt, float yawRateRadPerSec, List<SpinEvent> output)
    {
        if (!_open) return;

        float yawDegPerSec = yawRateRadPerSec * (180f / MathF.PI);
        bool rotatingNow = MathF.Abs(yawDegPerSec) >= _t.MinYawRateDeg;

        if (rotatingNow)
        {
            if (!_rotating)
            {
                if (time - _lastHitTime > _t.AttributionWindow)
                {
                    Close(output, award: false);
                    return;
                }
                _rotating = true;
            }
            _accumulatedDeg += yawDegPerSec * dt;
            _lastRotatingTime = time;

            var tier = Scoring.TierFor(_accumulatedDeg, _t.TierToleranceDeg);
            if (tier > _reported)
            {
                _reported = tier;
                output.Add(new SpinEvent(SpinEventKind.TierReached, _attacker, tier));
            }
        }
        else if (_rotating)
        {
            if (time - _lastRotatingTime >= _t.StopGrace)
            {
                Close(output, award: true);
                return;
            }
        }
        else if (time - _lastHitTime > _t.AttributionWindow)
        {
            Close(output, award: false);
            return;
        }

        if (time - _openTime >= _t.MaxDuration)
        {
            Close(output, award: _rotating);
        }
    }

    private void Close(List<SpinEvent> output, bool award)
    {
        if (award)
        {
            var tier = Scoring.TierFor(_accumulatedDeg, _t.TierToleranceDeg);
            if (tier != SpinTier.None)
                output.Add(new SpinEvent(SpinEventKind.Awarded, _attacker, tier));
        }
        _open = false;
        _rotating = false;
        _accumulatedDeg = 0f;
        _reported = SpinTier.None;
    }
}
