using System.Collections.Generic;
using System.Linq;

namespace DestructionRacing.Rules;

/// <summary>
/// Running order and finishing order of a race (plan §4.2). Each car's progress is the distance driven along the
/// lap since the start line (negative on the grid). A car finishes at Laps × LapLength. A wreck is a DNF.
/// When the race ends the order freezes: finishers by finishing time, then running cars by progress, then the DNFs
/// (the later a car was wrecked, the better its place).
/// </summary>
public sealed class RaceStandings
{
    private readonly Dictionary<int, float> _progress = new();
    private readonly Dictionary<int, double> _finishedAt = new();
    private readonly Dictionary<int, double> _retiredAt = new();
    private readonly List<int> _order = new();
    private bool _dirty = true;

    public RaceStandings(IEnumerable<int> cars, int laps, float lapLength)
    {
        foreach (int c in cars) _progress[c] = 0f;
        Laps = laps;
        LapLength = lapLength;
    }

    public int Laps { get; }
    public float LapLength { get; }
    public float RaceDistance => Laps * LapLength;
    public bool IsFrozen { get; private set; }
    public IReadOnlyCollection<int> Cars => _progress.Keys;

    public float Progress(int car) => _progress[car];
    public bool HasFinished(int car) => _finishedAt.ContainsKey(car);
    public bool IsRetired(int car) => _retiredAt.ContainsKey(car);
    public bool IsRacing(int car) => !HasFinished(car) && !IsRetired(car);
    public double? FinishTime(int car) => _finishedAt.TryGetValue(car, out double t) ? t : null;
    public int FinishedCount => _finishedAt.Count;

    /// <summary>The lap a car is on, 1..Laps (Laps once finished).</summary>
    public int LapOf(int car) => System.Math.Clamp((int)System.MathF.Floor(_progress[car] / LapLength) + 1, 1, Laps);

    /// <summary>Updates a car's progress. Returns true if this update made it cross the finish.</summary>
    public bool Update(int car, float progress, double time)
    {
        if (IsFrozen || !IsRacing(car)) return false;
        _progress[car] = progress;
        _dirty = true;
        if (progress >= RaceDistance)
        {
            _finishedAt[car] = time;
            return true;
        }
        return false;
    }

    /// <summary>A wrecked car is out of the race (DNF).</summary>
    public void Retire(int car, double time)
    {
        if (IsFrozen || !IsRacing(car)) return;
        _retiredAt[car] = time;
        _dirty = true;
    }

    /// <summary>Ends the race: the current order becomes the result.</summary>
    public void Freeze()
    {
        Rebuild();
        IsFrozen = true;
    }

    public IReadOnlyList<int> Order()
    {
        if (_dirty && !IsFrozen) Rebuild();
        return _order;
    }

    public int PositionOf(int car) => Order().ToList().IndexOf(car) + 1;

    /// <summary>The car in 1st place (the ×2 target), or null if nobody is racing or has finished.</summary>
    public int? Leader
    {
        get
        {
            var order = Order();
            return order.Count > 0 && !IsRetired(order[0]) ? order[0] : null;
        }
    }

    private void Rebuild()
    {
        _order.Clear();
        _order.AddRange(_finishedAt.OrderBy(kv => kv.Value).Select(kv => kv.Key));
        _order.AddRange(_progress.Keys.Where(IsRacing).OrderByDescending(c => _progress[c]));
        _order.AddRange(_retiredAt.OrderByDescending(kv => kv.Value).Select(kv => kv.Key));
        _dirty = false;
    }
}
