using System.Collections.Generic;

namespace DestructionRacing.Rules;

/// <summary>
/// Remembers who damaged a car recently; the wreck goes to whoever dealt the most damage
/// within the attribution window (plan §5.3). Walls are not recorded, so they never get credit.
/// </summary>
public sealed class WreckAttribution
{
    private readonly float _window;
    private readonly List<(int Attacker, double Time, float Damage)> _entries = new();

    public WreckAttribution(float windowSeconds)
    {
        _window = windowSeconds;
    }

    public void RecordDamage(int attackerId, double time, float damage)
    {
        if (damage <= 0f) return;
        _entries.Add((attackerId, time, damage));
        Prune(time);
    }

    /// <summary>Returns the credited attacker for a wreck at <paramref name="time"/>, or null.</summary>
    public int? ResolveWrecker(double time)
    {
        Prune(time);
        var totals = new Dictionary<int, float>();
        foreach (var e in _entries)
            totals[e.Attacker] = (totals.TryGetValue(e.Attacker, out float v) ? v : 0f) + e.Damage;

        int? best = null;
        float bestDamage = 0f;
        foreach (var (attacker, damage) in totals)
        {
            if (damage > bestDamage)
            {
                bestDamage = damage;
                best = attacker;
            }
        }
        return best;
    }

    private void Prune(double now) => _entries.RemoveAll(e => now - e.Time > _window);
}
