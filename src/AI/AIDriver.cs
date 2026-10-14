using DestructionRacing.Vehicles;

namespace DestructionRacing.AI;

/// <summary>Connects a <see cref="DerbyBrain"/> to a car through the same input interface as the player.</summary>
public sealed class AIDriver : ICarInput
{
    public AIDriver(DerbyBrain brain, IArenaView view)
    {
        Brain = brain;
        View = view;
    }

    public DerbyBrain Brain { get; }
    public IArenaView View { get; }

    public CarControls GetControls(Car car, float delta) => Brain.Think(car, View, delta);
}

/// <summary>Connects a <see cref="RaceBrain"/> to a car.</summary>
public sealed class RaceDriver : ICarInput
{
    public RaceDriver(RaceBrain brain, IRaceView view)
    {
        Brain = brain;
        View = view;
    }

    public RaceBrain Brain { get; }
    public IRaceView View { get; }

    public CarControls GetControls(Car car, float delta) => Brain.Think(car, View, delta);
}
