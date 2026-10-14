using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using DestructionRacing.AI;
using DestructionRacing.Core;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>
/// Sim harness (plan §17.1): runs derbies back to back, headless, with the PlayerProxy bot in the player's car,
/// and writes the engagement and frustration report (plan §17.4) to out/sims/.
///   --sims=N (default 20)  --seed=S (default 1)  --profile=average|skilled
/// Run with: tools/dev/headless.ps1 -Scene res://scenes/test/DerbySim.tscn -UserArgs '--sims=50'
/// </summary>
public partial class DerbySim : Node
{
    private readonly List<DerbyMetricsResult> _results = new();
    private int _sims = 20;
    private int _seed = 1;
    private int _index;
    private string _profile = "average";
    private DerbyMode? _current;
    private ulong _startTicks;

    public override void _Ready()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; // "1.5 s", not "1,5 s", in the report
        if (int.TryParse(DevTools.Instance?.Arg("sims"), out int n)) _sims = Math.Max(1, n);
        if (int.TryParse(DevTools.Instance?.Arg("seed"), out int s)) _seed = s;
        _profile = DevTools.Instance?.Arg("profile") ?? "average";
        _startTicks = Time.GetTicksMsec();
        GD.Print($"[Sim] {_sims} derbies, profile {_profile}, seeds {_seed}..{_seed + _sims - 1}");
        StartNext();
    }

    private void StartNext()
    {
        if (_current != null)
        {
            _current.QueueFree();
            _current = null;
        }
        if (_index >= _sims)
        {
            Report();
            return;
        }
        int seed = _seed + _index++;
        _current = new DerbyMode
        {
            Headless = true,
            ProxyPlayer = true,
            ProxyProfile = _profile == "skilled" ? Personality.SkilledPlayer : Personality.AverageFan,
            Seed = seed,
            CountdownTime = 0.5f,
            DumpEvery = double.TryParse(DevTools.Instance?.Arg("dump"), out double dump) ? dump : 0,
        };
        _current.Finished += OnFinished;
        AddChild(_current);
    }

    private void OnFinished(DerbyMode mode)
    {
        var r = mode.MetricsResult;
        _results.Add(r);
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"[Sim] #{r.Seed}: length {r.EventLength:0}s, first contact {r.FirstContactTime:0.0}s, longest idle {r.LongestIdle:0.0}s, " +
            $"player {(r.PlayerWrecked ? $"wrecked at {r.PlayerSurvival:0}s" : "survived")} place {r.PlayerPlace} pts {r.PlayerPoints}, " +
            $"hits/min {r.PlayerHitsPerMinute:0.0}, spins {r.Spins}, wrecks {r.Wrecks}{(r.TimedOut ? " TIMEOUT" : "")}"));
        CallDeferred(MethodName.StartNext);
    }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
        if (v.Count == 0) return double.NaN;
        return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
    }

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var v = values.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
        if (v.Count == 0) return double.NaN;
        return v[(int)Math.Clamp(Math.Ceiling(p * v.Count) - 1, 0, v.Count - 1)];
    }

    private void Report()
    {
        var r = _results;
        int n = r.Count;
        var wrecked = r.Where(x => x.PlayerWrecked).ToList();
        double share(Func<DerbyMetricsResult, bool> f) => n == 0 ? 0 : (double)r.Count(f) / n;
        var ci = CultureInfo.InvariantCulture;

        var rows = new List<(string Metric, string Value, string Target, bool? Pass)>
        {
            ("Time to first contact (median)", $"{Median(r.Select(x => x.FirstContactTime)):0.0} s", "< 3 s", Median(r.Select(x => x.FirstContactTime)) < 3),
            ("Longest idle stretch, ≥ 5 cars (median / p90)", $"{Median(r.Select(x => x.LongestIdle)):0.0} / {Percentile(r.Select(x => x.LongestIdle), 0.9):0.0} s", "≤ 8 s", Median(r.Select(x => x.LongestIdle)) <= 8),
            ("Player hits per minute, dealt + taken (median)", $"{Median(r.Select(x => x.PlayerHitsPerMinute)):0.0}", "≥ 8", Median(r.Select(x => x.PlayerHitsPerMinute)) >= 8),
            ("Player hits dealt per minute (median)", $"{Median(r.Select(x => x.PlayerHitsDealtPerMinute)):0.0}", "–", null),
            ("Event length (median)", $"{Median(r.Select(x => x.EventLength)):0} s", "120–240 s", Median(r.Select(x => x.EventLength)) is >= 120 and <= 240),
            ("Player survival (median)", $"{Median(r.Select(x => x.PlayerSurvival)):0} s", "60–150 s", Median(r.Select(x => x.PlayerSurvival)) is >= 60 and <= 150),
            ("Share of player points from deliberate hits", $"{Median(r.Select(x => x.DeliberatePointsShare)) * 100:0}% (median)", "≥ 70%", Median(r.Select(x => x.DeliberatePointsShare)) >= 0.7),
            ("Player wrecked within 30 s / 60 s", $"{share(x => x.PlayerWrecked && x.PlayerSurvival <= 30) * 100:0}% / {share(x => x.PlayerWrecked && x.PlayerSurvival <= 60) * 100:0}%", "≤ 5% / ≤ 15%",
                share(x => x.PlayerWrecked && x.PlayerSurvival <= 30) <= 0.05 && share(x => x.PlayerWrecked && x.PlayerSurvival <= 60) <= 0.15),
            ("Player wrecks with ≥ 3 attackers in the last 3 s", $"{(wrecked.Count == 0 ? 0 : 100.0 * wrecked.Count(x => x.PlayerWreckAttackers >= 3) / wrecked.Count):0}%", "≤ 10%",
                wrecked.Count == 0 || (double)wrecked.Count(x => x.PlayerWreckAttackers >= 3) / wrecked.Count <= 0.10),
            ("Player stuck or recovering", $"{(r.Sum(x => x.PlayerSurvival) > 0 ? 100 * r.Sum(x => x.PlayerStuckTime) / r.Sum(x => x.PlayerSurvival) : 0):0.0}% of time", "≤ 5%",
                r.Sum(x => x.PlayerSurvival) <= 0 || r.Sum(x => x.PlayerStuckTime) / r.Sum(x => x.PlayerSurvival) <= 0.05),
            ("Derbies that ended normally (no time-out)", $"{share(x => !x.TimedOut) * 100:0}%", "100%", r.All(x => !x.TimedOut)),
            ("Player wins / mean place", $"{share(x => x.PlayerPlace == 1) * 100:0}% / {r.Average(x => (double)x.PlayerPlace):0.0}", "–", null),
            ("Player points (median)", $"{Median(r.Select(x => (double)x.PlayerPoints)):0}", "–", null),
            ("Spins / wrecks per derby (mean)", $"{r.Average(x => x.Spins):0.0} / {r.Average(x => x.Wrecks):0.0}", "–", null),
            ("Winner survived (vs. last two wrecked together)", $"{share(x => x.WinnerSurvived) * 100:0}%", "–", null),
        };

        var md = new StringBuilder();
        md.AppendLine($"# Derby sim report — {n} derbies, PlayerProxy \"{_profile}\", seeds {_seed}..{_seed + n - 1}");
        md.AppendLine();
        md.AppendLine("| Metric | Value | Target | |");
        md.AppendLine("|---|---|---|---|");
        foreach (var (metric, value, target, pass) in rows)
            md.AppendLine($"| {metric} | {value} | {target} | {(pass == null ? "" : pass.Value ? "✅" : "❌")} |");
        md.AppendLine();
        md.AppendLine($"Wall-clock time: {(Time.GetTicksMsec() - _startTicks) / 1000.0:0} s.");
        string text = md.ToString();

        string dir = ProjectSettings.GlobalizePath("res://out/sims");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", ci);
        using (var f = FileAccess.Open($"{dir}/derby_{_profile}_{stamp}.md", FileAccess.ModeFlags.Write)) f?.StoreString(text);
        using (var f = FileAccess.Open($"{dir}/derby_{_profile}_{stamp}.json", FileAccess.ModeFlags.Write))
            f?.StoreString(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));

        foreach (var (metric, value, target, pass) in rows)
            GD.Print($"[Sim] {(pass == null ? "  " : pass.Value ? "OK" : "!!")} {metric}: {value} (target {target})");
        GD.Print($"[Sim] report: {dir}/derby_{_profile}_{stamp}.md");
        DevTools.QuitClean(GetTree(), 0);
    }
}
