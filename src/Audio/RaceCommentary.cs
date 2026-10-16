using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Modes;
using DestructionRacing.Rules;
using DestructionRacing.Tracks;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// What the announcer says in a race at Crossroads Speedway (plan §12): the intro for the layout and the event, the
/// green flag, crashes at the crossover, pile-ups and big hits, spins and wrecks with car numbers, the leader spun,
/// the player's own damage (DD2's "You've lost a wheel!"), pit stops, passes and side-by-side battles, the new
/// leader, the white flag and the finish. Each kind of call has its own cooldown; the <see cref="Announcer"/>
/// keeps lines from repeating.
/// </summary>
public partial class RaceCommentary : Node
{
    public RaceMode Mode { get; set; } = null!;
    public Announcer Announcer { get; set; } = null!;

    private readonly Dictionary<string, double> _cooldown = new();
    private readonly HashSet<string> _once = new();
    private readonly List<(double Time, int A, int B)> _recentHits = new();
    private readonly Random _rng = new(29);
    private int? _leader;
    private int _bestPlace = 99, _candidatePlace = 99;
    private double _candidateSince;
    private double _closeFor;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Mode.ImpactHappened += OnImpact;
        Mode.RefereeEventRaised += OnRefereeEvent;
        Mode.CountdownTick += OnCountdown;
        Mode.Started += OnStarted;
        Mode.Ended += OnEnded;
        Mode.Pits.Stopped += OnPitStop;
        CarVisual.Moment += OnMoment;
    }

    public override void _ExitTree()
    {
        Mode.ImpactHappened -= OnImpact;
        Mode.RefereeEventRaised -= OnRefereeEvent;
        Mode.CountdownTick -= OnCountdown;
        Mode.Started -= OnStarted;
        Mode.Ended -= OnEnded;
        Mode.Pits.Stopped -= OnPitStop;
        CarVisual.Moment -= OnMoment;
    }

    private void Call(string category, double cooldown, Announcer.Priority priority = Announcer.Priority.Normal, string? key = null)
    {
        key ??= category;
        double now = Mode.Time;
        if (_cooldown.TryGetValue(key, out double until) && now < until) return;
        _cooldown[key] = now + cooldown;
        Announcer.Say(category, priority);
    }

    private void CallOnce(string category, Announcer.Priority priority = Announcer.Priority.High)
    {
        if (_once.Add(category)) Announcer.Say(category, priority);
    }

    private void CallNumber(string prefix, Car car, string generic, double cooldown, Announcer.Priority priority)
    {
        string id = $"{prefix}_{car.Number:00}";
        double now = Mode.Time;
        if (_cooldown.TryGetValue(generic, out double until) && now < until) return;
        _cooldown[generic] = now + cooldown;
        if (Announcer.HasLine(id) && _rng.NextDouble() < 0.7) Announcer.SayLine(id, priority);
        else if (generic.Length > 0) Announcer.Say(generic, priority);
    }

    private bool IsPlayer(Car? c) => c != null && c == Mode.PlayerCar;

    // ------------------------------------------------------------------ events

    private void OnCountdown(int n)
    {
        if (n != 3) return;
        // One intro: the speedway, the layout or the event, whichever the dice pick.
        string pick = _rng.Next(3) switch
        {
            0 => "race_intro",
            1 => Mode.IsStockCar ? "race_intro_stock" : Mode.LayoutId == SpeedwayLayout.Oval ? "race_intro_oval" : "race_intro_fig8",
            _ => Mode.LayoutId == SpeedwayLayout.Oval ? "race_intro_oval" : "race_intro_fig8",
        };
        CallOnce(pick, Announcer.Priority.Normal);
    }

    private void OnStarted() => Announcer.Say("race_start", Announcer.Priority.High);

    private void OnImpact(Impact i)
    {
        if (i.Energy < 9_000f || i.B == null) return;
        double now = Mode.Time;
        _recentHits.Add((now, i.A.Id, i.B.Id));
        _recentHits.RemoveAll(h => now - h.Time > 1.5);
        if (_recentHits.SelectMany(h => new[] { h.A, h.B }).Distinct().Count() >= 4)
        {
            Call("pileup", 30.0);
            return;
        }
        // Crossing traffic meeting in the middle of the figure-8.
        if (Mode.Layout.HasCrossover && i.Energy >= 20_000f
            && Mode.Layout.InCrossover(Mode.TrackPos(i.A).S) && Mode.Layout.InCrossover(Mode.TrackPos(i.B).S))
        {
            Vector3 fa = -i.A.GlobalTransform.Basis.Z, fb = -i.B.GlobalTransform.Basis.Z;
            fa.Y = fb.Y = 0f;
            if (fa.Normalized().Dot(fb.Normalized()) < 0.8f)
            {
                Call("race_crossover", 14.0, Announcer.Priority.High);
                return;
            }
        }
        bool player = IsPlayer(i.A) || IsPlayer(i.B);
        bool playerAttacked = IsPlayer(i.A) ? i.Attacker is HitAttacker.A or HitAttacker.Both
            : IsPlayer(i.B) && i.Attacker is HitAttacker.B or HitAttacker.Both;
        if (i.Energy >= 120_000f) Call("huge_hit", 18.0);
        else if (player && playerAttacked && i.Energy >= 45_000f) Call("player_big_hit", 10.0);
        else if (i.Energy >= 60_000f) Call("big_hit", 12.0, Announcer.Priority.Filler);
    }

    private void OnRefereeEvent(RefereeEvent e)
    {
        var player = Mode.PlayerCar;
        switch (e.Kind)
        {
            case RefereeEventKind.SpinScored when e.Doubled:
                Call("race_leader_spun", 20.0, Announcer.Priority.High, "spin");
                break;
            case RefereeEventKind.SpinScored when e.Car == player.Id:
                if (e.Tier == SpinTier.Full) Call("spin_360", 10.0, Announcer.Priority.High, "spin");
                else Call("player_spin", 10.0, Announcer.Priority.Normal, "spin");
                break;
            case RefereeEventKind.SpinScored when e.Other == player.Id:
                Call("player_spun", 20.0);
                break;
            case RefereeEventKind.WheelLost when e.Car == player.Id:
                Call("player_lost_wheel", 15.0, Announcer.Priority.High);
                break;
            case RefereeEventKind.Damage when e.Car == player.Id:
                PlayerDamage();
                break;
            case RefereeEventKind.Wrecked:
            {
                var victim = Mode.CarById(e.Car);
                if (victim == null) break;
                if (e.Car == player.Id) Announcer.Say("player_wrecked", Announcer.Priority.Critical);
                else if (e.Other == player.Id) Call("player_wrecks_car", 4.0, Announcer.Priority.High);
                else CallNumber("wrecked_num", victim, "wreck_generic", 8.0, Announcer.Priority.Normal);
                break;
            }
        }
    }

    private void PlayerDamage()
    {
        var p = Mode.PlayerCar;
        if (p.IsWrecked) return;
        float front = p.Health(DamageZone.Front);
        if (front < 0.08f) CallOnce("player_on_fire", Announcer.Priority.Critical);
        else if (front < 0.25f) CallOnce("player_front_critical");
        else if (front < 0.55f) CallOnce("player_smoking", Announcer.Priority.Normal);
        if (p.Health(DamageZone.Rear) < 0.25f) CallOnce("player_rear_critical");
        if (p.Health(DamageZone.Left) < 0.25f || p.Health(DamageZone.Right) < 0.25f) CallOnce("player_side_critical");
    }

    private void OnMoment(CarVisual visual, string what, Vector3 at)
    {
        if (visual.Car != Mode.PlayerCar || Mode.PlayerCar.IsWrecked) return;
        switch (what)
        {
            case "hood": Call("player_lost_hood", 60.0, Announcer.Priority.High); break;
            case "bumper": Call("player_lost_bumper", 40.0, Announcer.Priority.Normal); break;
            case "glass": Call("player_glass", 45.0, Announcer.Priority.Normal); break;
            case "roll": Call("rollover", 15.0, Announcer.Priority.High); break;
        }
    }

    private void OnPitStop(PitStops.Stop stop)
    {
        if (stop.Car == Mode.PlayerCar) Call("player_pit", 30.0, Announcer.Priority.High);
        else Call("race_pit", 45.0, Announcer.Priority.Filler);
    }

    private void OnEnded()
    {
        var standings = Mode.Standings;
        int pid = Mode.PlayerCar.Id;
        if (standings.HasFinished(pid))
        {
            int place = standings.PositionOf(pid);
            Announcer.Say(place == 1 ? "player_race_win" : place <= 3 ? "player_podium" : "player_finish", Announcer.Priority.Critical);
            return;
        }
        var order = standings.Order();
        if (order.Count > 0 && standings.HasFinished(order[0]) && Mode.CarById(order[0]) is { } winner
            && Announcer.HasLine($"race_winner_num_{winner.Number:00}"))
            Announcer.SayLine($"race_winner_num_{winner.Number:00}", Announcer.Priority.Critical);
        else Announcer.Say("race_results", Announcer.Priority.Critical);
    }

    public override void _Process(double delta)
    {
        if (Mode.IsOver || Mode.CountdownLeft > 0) return;
        double now = Mode.Time;
        var standings = Mode.Standings;
        var player = Mode.PlayerCar;

        // A new leader on the road.
        int? leader = standings.Leader;
        if (leader != _leader && leader is int id && now > 8.0 && Mode.CarById(id) is { } car)
        {
            if (car == player) Call("player_overtake", 25.0, Announcer.Priority.Normal);
            else CallNumber("leader_num", car, "", 20.0, Announcer.Priority.Filler);
        }
        _leader = leader;

        // The white flag for the leader.
        if (leader is int lid && standings.LapOf(lid) >= Mode.Laps && !standings.HasFinished(lid)) CallOnce("race_final_lap");

        if (Mode.PlayerOut || standings.HasFinished(player.Id)) return;
        // Passes: a new best place, held for two seconds.
        int place = standings.PositionOf(player.Id);
        if (place != _candidatePlace)
        {
            _candidatePlace = place;
            _candidateSince = now;
        }
        else if (place < _bestPlace && now - _candidateSince > 2.0 && now > 15.0)
        {
            if (_bestPlace < 99) Call("player_overtake", 25.0, Announcer.Priority.Filler);
            _bestPlace = place;
        }
        else if (place < _bestPlace && now <= 15.0) _bestPlace = place;

        // Side by side at speed for a couple of seconds.
        var (ps, pl) = Mode.TrackPos(player);
        bool close = player.ForwardSpeed > 20f && Mode.Cars.Any(c => c != player && Mode.IsRunning(c) && !Mode.IsGhost(c)
            && MathF.Abs(Mode.Track.Delta(ps, Mode.TrackPos(c).S)) < 5f && MathF.Abs(Mode.TrackPos(c).Lateral - pl) < 4f);
        _closeFor = close ? _closeFor + (float)delta : 0;
        if (_closeFor > 2.0) Call("race_close", 40.0, Announcer.Priority.Filler);
    }
}
