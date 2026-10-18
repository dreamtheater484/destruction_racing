using DestructionRacing.Modes;
using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// What the announcer says in Time Trial (plan §12): a short intro, each medal the first time you reach it this
/// session, a new best lap, and a lap spoilt by leaving the track. Quiet otherwise: it is just you and the clock.
/// </summary>
public partial class TimeTrialCommentary : Node
{
    public TimeTrialMode Mode { get; set; } = null!;
    public Announcer Announcer { get; set; } = null!;

    private int _bestMedal;
    private int _validLaps;
    private bool _hadBest, _wasInvalid;
    private double _clock, _invalidQuietUntil;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _hadBest = Mode.BestLap != null;
        Mode.Started += OnStarted;
        Mode.LapCompleted += OnLap;
    }

    public override void _ExitTree()
    {
        Mode.Started -= OnStarted;
        Mode.LapCompleted -= OnLap;
    }

    private void OnStarted() => Announcer.Say("tt_intro", Announcer.Priority.Normal, maxWait: 4.0);

    private void OnLap(double lap, bool valid, int place, int medal)
    {
        if (!valid) return;
        _validLaps++;
        if (medal > _bestMedal)
        {
            _bestMedal = medal;
            Announcer.Say(medal switch { 3 => "tt_gold", 2 => "tt_silver", _ => "tt_bronze" }, Announcer.Priority.High);
        }
        // The first lap of a car on a layout is its best by default: not worth a call.
        else if (place == 1 && (_hadBest || _validLaps > 1)) Announcer.Say("tt_best", Announcer.Priority.Normal);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        bool invalid = Mode.LapInvalid;
        if (invalid && !_wasInvalid && _clock > _invalidQuietUntil)
        {
            _invalidQuietUntil = _clock + 25.0;
            Announcer.Say("tt_invalid", Announcer.Priority.Filler);
        }
        _wasInvalid = invalid;
    }
}
