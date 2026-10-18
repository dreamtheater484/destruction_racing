using System.Collections.Generic;
using System.Linq;

namespace DestructionRacing.Rules;

/// <summary>
/// The Bowl's finishing order (plan §4.1): the last car still running wins, everyone else is placed in
/// reverse elimination order. Cars wrecked in the same step are ordered by points (more points = better place).
/// </summary>
public sealed class DerbyStandings
{
    private readonly HashSet<int> _running;
    private readonly Dictionary<int, int> _place = new();
    private readonly Dictionary<int, double> _eliminatedAt = new();

    public DerbyStandings(IEnumerable<int> cars)
    {
        _running = new HashSet<int>(cars);
        Field = _running.Count;
    }

    public int Field { get; }
    public int Remaining => _running.Count;
    public IReadOnlyCollection<int> Running => _running;
    public bool IsOver => Winner != null;

    /// <summary>The car in 1st place once the derby is over.</summary>
    public int? Winner { get; private set; }

    /// <summary>True if the winner is still running (earns the survivor bonus); false if the last cars wrecked together.</summary>
    public bool WinnerSurvived { get; private set; }

    public bool IsRunning(int car) => _running.Contains(car);
    public int? PlaceOf(int car) => _place.TryGetValue(car, out int p) ? p : null;
    public double? EliminatedAt(int car) => _eliminatedAt.TryGetValue(car, out double t) ? t : null;

    /// <summary>
    /// Eliminates the given cars (wrecked in the same step). Returns each car's place.
    /// When one car is left it wins; when none are left the best-placed of this batch wins.
    /// </summary>
    public IReadOnlyList<(int Car, int Place)> Eliminate(double time, IEnumerable<(int Car, int Points)> batch)
    {
        var result = new List<(int, int)>();
        if (Winner != null) return result;
        var ordered = batch.Where(b => _running.Contains(b.Car)).OrderBy(b => b.Points).ToList();
        foreach (var (car, _) in ordered)
        {
            int place = _running.Count;
            _running.Remove(car);
            _place[car] = place;
            _eliminatedAt[car] = time;
            result.Add((car, place));
        }

        if (_running.Count == 1)
        {
            int survivor = _running.First();
            _place[survivor] = 1;
            Winner = survivor;
            WinnerSurvived = true;
        }
        else if (_running.Count == 0 && result.Count > 0)
        {
            Winner = result[^1].Item1;
            WinnerSurvived = false;
        }
        return result;
    }

    /// <summary>Ends the derby early (e.g. a time limit): the running cars are placed by points, best first.</summary>
    public void EndByPoints(IReadOnlyDictionary<int, int> points, IReadOnlyDictionary<int, float>? health = null)
    {
        if (Winner != null) return;
        // A time limit (plan §4.1): points, then the car in the best shape.
        var ordered = _running.OrderByDescending(c => points.TryGetValue(c, out int p) ? p : 0)
            .ThenByDescending(c => health != null && health.TryGetValue(c, out float h) ? h : 0f).ToList();
        for (int i = 0; i < ordered.Count; i++) _place[ordered[i]] = i + 1;
        Winner = ordered.Count > 0 ? ordered[0] : null;
        WinnerSurvived = Winner != null;
    }

    /// <summary>Cars in finishing order (place 1 first); cars without a place yet are left out.</summary>
    public IReadOnlyList<int> FinishingOrder() => _place.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
}
