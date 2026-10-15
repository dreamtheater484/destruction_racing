using System;
using System.Linq;
using DestructionRacing.Rules;
using DestructionRacing.Tracks;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>
/// The derby's broadcast layer (plan §4.1 "escalating finale", §11 juice): listens to <see cref="DerbyMode"/> and
/// drives the crowd, the banners at 5/3/2 cars and the final duel, the floodlight flare, camera punch and shake,
/// the chromatic flash and the slow-motion moments. Sound and the announcer hang off the same events
/// (<see cref="Audio"/>). Not created in headless sims, so it can never change the outcome.
/// </summary>
public partial class DerbyPresenter : Node
{
    public const float BigHit = 30_000f, HugeHit = 90_000f;

    public DerbyMode Mode { get; set; } = null!;
    /// <summary>Slow motion (the Purist preset turns it off).</summary>
    public bool SlowMotion { get; set; } = true;

    public event Action<bool>? SlowMoChanged;

    private ScreenFx _fx = null!;
    private Crowd? Crowd => Mode.Arena.Crowd;
    private double _lastSlowMo = -100;
    private float _slowMoLeft;
    private double _baseTimeScale = 1.0;
    private Vector3 _action;
    private double _actionTime = -10;
    private bool _duelShown;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _fx = new ScreenFx();
        AddChild(_fx);
        Mode.ImpactHappened += OnImpact;
        Mode.RefereeEventRaised += OnRefereeEvent;
        Mode.RemainingChanged += OnRemaining;
        Mode.Finished += OnFinished;
        Mode.Started += () => Crowd?.Pulse(0.35f);
        CarVisual.Moment += OnMoment;
        if (Crowd != null) Crowd.BaseExcitement = 0.22f;
    }

    public override void _ExitTree()
    {
        Mode.ImpactHappened -= OnImpact;
        Mode.RefereeEventRaised -= OnRefereeEvent;
        Mode.RemainingChanged -= OnRemaining;
        Mode.Finished -= OnFinished;
        CarVisual.Moment -= OnMoment;
    }

    private void OnMoment(CarVisual visual, string what, Vector3 at)
    {
        if (what != "roll") return;
        Crowd?.Pulse(0.35f);
        if (visual.Car == Mode.PlayerCar && !Mode.PlayerCar.IsWrecked) Mode.Hud?.Popup("ROLLOVER!", HudStyle.Orange);
    }

    private bool Involves(Impact i, Car? car) => car != null && (i.A == car || i.B == car);

    private void OnImpact(Impact i)
    {
        if (i.Energy < 6_000f) return;
        // The crowd reacts to every real hit, more to big ones, and looks at the latest one.
        if (i.Energy >= BigHit || Mode.Time - _actionTime > 1.5)
        {
            _action = i.Point;
            _actionTime = Mode.Time;
        }
        Crowd?.Pulse(Mathf.Clamp((i.Energy - 6_000f) / 150_000f, 0.02f, 0.4f));

        var rig = Mode.Rig;
        if (rig == null || !Involves(i, rig.Target)) return;
        if (i.Energy >= BigHit) rig.Punch(Mathf.Clamp(i.Energy / 140_000f, 0.25f, 1f));
        if (i.Energy >= HugeHit && Involves(i, Mode.PlayerCar))
        {
            var cam = rig.GetViewport().GetCamera3D();
            var screen = cam != null ? cam.UnprojectPosition(i.Point) - rig.GetViewport().GetVisibleRect().Size * 0.5f : Vector2.Right;
            _fx.Flash(Mathf.Clamp(i.Energy / 160_000f, 0.5f, 1f), screen);
        }
    }

    private void OnRefereeEvent(RefereeEvent e)
    {
        var player = Mode.PlayerCar;
        switch (e.Kind)
        {
            case RefereeEventKind.SpinScored:
                Crowd?.Pulse(e.Tier == SpinTier.Full ? 0.3f : 0.12f);
                break;
            case RefereeEventKind.Wrecked:
                Crowd?.Pulse(e.Other == player.Id ? 0.55f : 0.4f);
                if (Mode.CarById(e.Car) is { } victim)
                {
                    _action = victim.GlobalPosition;
                    _actionTime = Mode.Time;
                }
                // Slow motion when the player wrecks someone or is wrecked (once per 10 s, not when skipping).
                if (e.Other == player.Id || e.Car == player.Id) TrySlowMo();
                break;
        }
    }

    private void OnRemaining(int remaining)
    {
        if (Crowd != null) Crowd.BaseExcitement = remaining switch
        {
            <= 2 => 0.8f,
            <= 3 => 0.65f,
            <= 5 => 0.5f,
            <= 10 => 0.35f,
            _ => 0.22f,
        };
        if (Mode.IsOver) return;
        switch (remaining)
        {
            case 5:
                Mode.Hud?.Banner("FINAL FIVE", "five cars still running");
                Mode.Arena.Flare();
                break;
            case 3:
                Mode.Hud?.Banner("LAST THREE", "who will survive the Bowl?");
                Mode.Arena.Flare();
                break;
            case 2 when !_duelShown:
                _duelShown = true;
                var duel = Mode.Cars.Where(Mode.IsRunning).Select(c => c == Mode.PlayerCar ? "YOU" : $"#{c.Number}").ToList();
                Mode.Hud?.Banner("FINAL DUEL", duel.Count == 2 ? $"{duel[0]}   vs   {duel[1]}" : "", HudStyle.Red);
                Mode.Arena.Flare();
                Crowd?.Pulse(0.5f);
                break;
        }
    }

    private void OnFinished(DerbyMode mode)
    {
        Crowd?.Pulse(1f);
        if (Crowd != null) Crowd.BaseExcitement = 0.75f;
        EndSlowMo();
        if (mode.Standings.Winner is int w && mode.CarById(w) is { } winner && mode.Standings.WinnerSurvived)
            mode.Hud?.Banner(winner == mode.PlayerCar ? "YOU WIN!" : $"#{winner.Number} WINS", "the last car running in the Bowl");
    }

    private void TrySlowMo()
    {
        if (!SlowMotion || Mode.Skipping || Mode.Time - _lastSlowMo < 10.0 || _slowMoLeft > 0f) return;
        _lastSlowMo = Mode.Time;
        _baseTimeScale = Engine.TimeScale;
        _slowMoLeft = 0.9f;                   // real seconds at 35 %: about 0.3 s of the crash
        Engine.TimeScale = _baseTimeScale * 0.35;
        _fx.SlowMo(true);
        SlowMoChanged?.Invoke(true);
    }

    private void EndSlowMo()
    {
        if (_slowMoLeft <= 0f) return;
        _slowMoLeft = 0f;
        if (!Mode.Skipping) Engine.TimeScale = _baseTimeScale;
        _fx.SlowMo(false);
        SlowMoChanged?.Invoke(false);
    }

    public override void _Process(double delta)
    {
        float rdt = (float)(delta / Math.Max(Engine.TimeScale, 0.01));
        if (_slowMoLeft > 0f)
        {
            _slowMoLeft -= rdt;
            if (_slowMoLeft <= 0f || Mode.Skipping) { _slowMoLeft = 0.0001f; EndSlowMo(); }
        }
        if (Crowd != null)
        {
            // Look at the latest action, else at the camera's car.
            var focus = Mode.Time - _actionTime < 3.0 ? _action : Mode.Rig?.Target?.GlobalPosition ?? Vector3.Zero;
            Crowd.ActionPoint = focus;
        }
    }
}
