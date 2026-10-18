using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Modes;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// What the announcer says in the derby (plan §12): the intro and the start, big hits and pile-ups, spins and
/// wrecks with car numbers, damage calls about the player's own car as in DD2 ("You've lost a wheel!", "You're on
/// FIRE!"), the points leader, the final cars and the winner. Each kind of call has its own cooldown, and the
/// <see cref="Announcer"/> keeps lines from repeating.
/// </summary>
public partial class DerbyCommentary : Node
{
    public DerbyMode Mode { get; set; } = null!;
    public Announcer Announcer { get; set; } = null!;

    private readonly Dictionary<string, double> _cooldown = new();
    private readonly HashSet<string> _once = new();
    private readonly List<(double Time, int A, int B)> _recentHits = new();
    private readonly Random _rng = new(23);
    private int? _leader;
    private double _lastHitAnywhere;
    private bool _crunchCalled;
    private int _minutesSurvived;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Mode.ImpactHappened += OnImpact;
        Mode.RefereeEventRaised += OnRefereeEvent;
        Mode.CountdownTick += OnCountdown;
        Mode.Started += OnStarted;
        Mode.RemainingChanged += OnRemaining;
        Mode.Finished += OnFinished;
        CarVisual.Moment += OnMoment;
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
    }

    /// <summary>Says a line of the category unless one of the same kind was said in the last <paramref name="cooldown"/> s.</summary>
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

    /// <summary>A line with the car's number if there is one ("Number twenty-three is out!"), else the generic call.</summary>
    private void CallNumber(string prefix, Car car, string generic, double cooldown, Announcer.Priority priority)
    {
        string id = $"{prefix}_{car.Number:00}";
        if (Announcer.HasLine(id) && _rng.NextDouble() < 0.6)
        {
            double now = Mode.Time;
            if (_cooldown.TryGetValue(generic, out double until) && now < until) return;
            _cooldown[generic] = now + cooldown;
            Announcer.SayLine(id, priority);
        }
        else Call(generic, cooldown, priority);
    }

    private bool IsPlayer(Car? c) => c != null && c == Mode.PlayerCar;

    // ------------------------------------------------------------------ events

    private void OnCountdown(int n)
    {
        if (n != 3) return;
        // One intro: the Duel's or Total Destruction's own, the championship's, or the Bowl's (the twenty-car
        // lines only with a full field).
        string pick = Mode.Rival != null ? "duel_intro"
            : Mode.TotalDestruction ? "td_intro"
            : Session.IsFinale ? "champ_finale"
            : Session.InChampionship && _rng.NextDouble() < 0.5 ? "champ_round"
            : Mode.CarCount >= 20 && _rng.NextDouble() < 0.35 ? "intro_full" : "intro";
        CallOnce(pick, Announcer.Priority.Normal);
    }

    private void OnStarted() => Announcer.Say("start", Announcer.Priority.High);

    private void OnImpact(Impact i)
    {
        if (i.Energy < 9_000f || i.B == null) return;
        double now = Mode.Time;
        _lastHitAnywhere = now;
        _recentHits.Add((now, i.A.Id, i.B.Id));
        _recentHits.RemoveAll(h => now - h.Time > 1.5);

        // The opening crunch: the pack meets in the middle.
        if (!_crunchCalled && now < 8.0 && _recentHits.Count >= 4)
        {
            _crunchCalled = true;
            Announcer.Say("opening_crunch", Announcer.Priority.High, maxWait: 5.0);
            return;
        }
        // A pile-up: four or more cars tangled within a second and a half.
        if (now > 8.0 && _recentHits.SelectMany(h => new[] { h.A, h.B }).Distinct().Count() >= 4)
        {
            Call("pileup", 30.0);
            return;
        }
        bool player = IsPlayer(i.A) || IsPlayer(i.B);
        bool playerAttacked = IsPlayer(i.A) ? i.Attacker is HitAttacker.A or HitAttacker.Both
            : IsPlayer(i.B) && i.Attacker is HitAttacker.B or HitAttacker.Both;
        var pc = Mode.PlayerCar;
        if (player && playerAttacked && i.Energy >= 30_000f && (pc.LastControls.Reverse || pc.ForwardSpeed < -1f))
            Call("reverse_ram", 25.0);
        else if (i.Energy >= 120_000f) Call("huge_hit", 18.0, Announcer.Priority.Normal);
        else if (player && playerAttacked && i.Energy >= 45_000f) Call("player_big_hit", 9.0);
        else if (i.Energy >= 60_000f) Call("big_hit", 9.0, Announcer.Priority.Filler);
    }

    private void OnRefereeEvent(RefereeEvent e)
    {
        var player = Mode.PlayerCar;
        switch (e.Kind)
        {
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
                else if (Mode.Remaining > 2) CallNumber("wrecked_num", victim, "wreck_generic", 6.0, Announcer.Priority.Normal);
                break;
            }
        }
    }

    /// <summary>DD2's damage calls about your own car, each once (or rarely) per event.</summary>
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
            case "headlamp" when Mode.Arena.FloodlightsOn: Call("player_lights", 90.0, Announcer.Priority.Filler); break;
            case "roll": Call("rollover", 15.0, Announcer.Priority.High); break;
        }
    }

    private void OnRemaining(int remaining)
    {
        switch (remaining)
        {
            case 10: Announcer.Say("cars_left_10", Announcer.Priority.Normal); break;
            case 5: Announcer.Say("cars_left_5", Announcer.Priority.High); break;
            case 3: Announcer.Say("cars_left_3", Announcer.Priority.High); break;
            case 2: Announcer.Say("final_duel", Announcer.Priority.Critical); break;
        }
    }

    private void OnFinished(DerbyMode mode)
    {
        if (Announcer.Log) GD.Print($"[Commentary] finished at {mode.Time:0.0} s: winner {mode.Standings.Winner}, player out {mode.PlayerOut}");
        // The end calls wait for the wreck call that usually comes with the finish.
        if (mode.TotalDestruction && mode.PlayerOut)
        {
            Announcer.Say(mode.TotalDestructionRecord ? "td_record" : "td_end", Announcer.Priority.Critical, maxWait: 8.0);
            return;
        }
        if (mode.Standings.Winner is not int w || mode.CarById(w) is not { } winner) return;
        if (mode.Rival != null)
        {
            bool won = winner == mode.PlayerCar && mode.Standings.WinnerSurvived;
            Announcer.Say(won ? "duel_player_wins" : "duel_player_loses", Announcer.Priority.Critical, maxWait: 8.0);
            return;
        }
        if (winner == mode.PlayerCar && mode.Standings.WinnerSurvived) Announcer.Say("player_wins", Announcer.Priority.Critical, maxWait: 8.0);
        else if (Announcer.HasLine($"winner_num_{winner.Number:00}")) Announcer.SayLine($"winner_num_{winner.Number:00}", Announcer.Priority.Critical, maxWait: 8.0);
        else Announcer.Say("results", Announcer.Priority.Critical);
    }

    public override void _Process(double delta)
    {
        if (Mode.IsOver || Mode.CountdownLeft > 0) return;
        double now = Mode.Time;
        // The points leader changes hands.
        int? leader = Mode.Referee.Leader;
        if (leader != _leader && leader is int id && now > 10.0)
        {
            if (id == Mode.PlayerCar.Id) Call("player_leader", 40.0, Announcer.Priority.Normal);
            else if (Mode.CarById(id) is { } car) CallNumber("leader_num", car, "new_leader", 25.0, Announcer.Priority.Filler);
        }
        _leader = leader;
        // Ganged up on (not in Total Destruction, where everyone always is).
        if (!Mode.TotalDestruction && Mode.Hud is { Hunters: >= 3 } && !Mode.PlayerOut) Call("hunted", 35.0);
        // A lull while plenty of cars are left: get stuck in! In the Duel, two cars circling.
        if (Mode.Remaining >= 5 && now - _lastHitAnywhere > 5.0 && now > 12.0) Call("lull", 25.0, Announcer.Priority.Filler);
        if (Mode.Rival != null && now - _lastHitAnywhere > 7.0 && now > 10.0) Call("duel_lull", 30.0, Announcer.Priority.Filler);
        // Total Destruction: every minute survived.
        if (Mode.TotalDestruction && !Mode.PlayerOut && (int)(now / 60.0) > _minutesSurvived)
        {
            _minutesSurvived = (int)(now / 60.0);
            Announcer.Say(_minutesSurvived switch { 1 => "td_one_minute", 2 => "td_two_minutes", 3 => "td_three_minutes", _ => "td_still_alive" },
                Announcer.Priority.High, maxWait: 5.0);
        }
    }
}
