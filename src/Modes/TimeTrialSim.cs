using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using DestructionRacing.AI;
using DestructionRacing.Core;
using DestructionRacing.Tracks;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>
/// Time Trial sims (plan §4.4, §17: "gold is reachable by skilled, not by average"): the "skilled" and "average"
/// PlayerProxy each drive flying laps in every car, headless, and the medal targets follow from their best laps:
/// gold just above the skilled bot's best, silver and bronze above the average bot's.
///   --layout=oval|figure8  --reverse  --car=ID (default: all nine)  --sims=N seeds per car and profile (default 2)
///   --laps=L timed laps per run (default 4)  --writetargets (merged into assets/speedway/time_trial/&lt;layout&gt;_&lt;dir&gt;.json)
/// </summary>
public partial class TimeTrialSim : Node
{
    public const string TargetsDir = "res://assets/speedway/time_trial";

    private readonly List<(string Car, string Profile, int Seed)> _queue = new();
    private readonly Dictionary<(string Car, string Profile), List<double>> _laps = new();
    private SpeedwayLayout _layout = SpeedwayLayout.Figure8;
    private bool _reverse;
    private int _lapsPerRun = 4;
    private TimeTrialMode? _current;
    private (string Car, string Profile) _currentKey;
    private ulong _startTicks;

    public override void _Ready()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var dev = DevTools.Instance;
        if (dev?.Arg("layout") is { } layout) _layout = layout.ToLowerInvariant() == "oval" ? SpeedwayLayout.Oval : SpeedwayLayout.Figure8;
        _reverse = dev?.Arg("reverse") != null;
        int seeds = int.TryParse(dev?.Arg("sims"), out int n) ? Math.Max(1, n) : 2;
        if (int.TryParse(dev?.Arg("laps"), out int l)) _lapsPerRun = Math.Max(1, l);
        var cars = dev?.Arg("car") is { } one ? new[] { one } : Vehicles.CarCatalog.Ids;
        foreach (string car in cars)
            foreach (string profile in new[] { "skilled", "average" })
                for (int s = 1; s <= seeds; s++)
                    _queue.Add((car, profile, s));
        _startTicks = Time.GetTicksMsec();
        GD.Print($"[TT] {_queue.Count} runs on {RaceLayout.DisplayName(_layout, _reverse)}, {_lapsPerRun} laps each");
        StartNext();
    }

    private void StartNext()
    {
        if (_current != null)
        {
            _current.QueueFree();
            _current = null;
        }
        if (_queue.Count == 0)
        {
            Report();
            return;
        }
        var (car, profile, seed) = _queue[0];
        _queue.RemoveAt(0);
        _currentKey = (car, profile);
        _current = new TimeTrialMode
        {
            Headless = true,
            ProxyPlayer = true,
            ProxyProfile = profile == "skilled" ? Personality.SkilledPlayer : Personality.AverageFan,
            Seed = seed,
            CarId = car,
            LayoutId = _layout,
            Reverse = _reverse,
            MaxLaps = _lapsPerRun,
            MaxDuration = 60.0 + _lapsPerRun * 90.0,
            Persist = false,
        };
        _current.Finished += OnFinished;
        AddChild(_current);
    }

    private void OnFinished(TimeTrialMode mode)
    {
        if (!_laps.TryGetValue(_currentKey, out var list)) _laps[_currentKey] = list = new List<double>();
        list.AddRange(mode.LapTimes);
        GD.Print($"[TT] {_currentKey.Car} {_currentKey.Profile} seed {mode.Seed}: {string.Join("  ", mode.LapTimes.Select(TimeTrialTargets.Format))}");
        CallDeferred(MethodName.StartNext);
    }

    private void Report()
    {
        string dirName = $"{(_layout == SpeedwayLayout.Oval ? "oval" : "figure8")}_{(_reverse ? "rev" : "fwd")}";
        var md = new StringBuilder();
        md.AppendLine($"# Time Trial sim report — {RaceLayout.DisplayName(_layout, _reverse)}");
        md.AppendLine();
        md.AppendLine("Best laps of the PlayerProxy and the medal targets that follow (gold: skilled best + 0.4 %; silver: average best + 1 %; bronze: average best + 5 %).");
        md.AppendLine();
        md.AppendLine("| Car | Skilled best | Average best | Gold | Silver | Bronze | Gold: skilled yes, average no |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        var targets = new Dictionary<string, double[]>();
        int pass = 0, total = 0;
        foreach (string car in _laps.Keys.Select(k => k.Car).Distinct())
        {
            double skilled = _laps.TryGetValue((car, "skilled"), out var sk) && sk.Count > 0 ? sk.Min() : double.NaN;
            double average = _laps.TryGetValue((car, "average"), out var av) && av.Count > 0 ? av.Min() : double.NaN;
            if (double.IsNaN(skilled) || double.IsNaN(average)) continue;
            double gold = Math.Round(skilled * 1.004, 2);
            double silver = Math.Round(Math.Max(average * 1.01, gold + 0.3), 2);
            double bronze = Math.Round(Math.Max(average * 1.05, silver + 0.6), 2);
            bool ok = skilled <= gold && average > gold;
            total++;
            if (ok) pass++;
            targets[TimeTrialRecords.Key(_layout, _reverse, car)] = new[] { gold, silver, bronze };
            md.AppendLine($"| {car} | {TimeTrialTargets.Format(skilled)} | {TimeTrialTargets.Format(average)} | {TimeTrialTargets.Format(gold)} | " +
                          $"{TimeTrialTargets.Format(silver)} | {TimeTrialTargets.Format(bronze)} | {(ok ? "✅" : "❌")} |");
        }
        md.AppendLine();
        md.AppendLine($"Wall-clock time: {(Time.GetTicksMsec() - _startTicks) / 1000.0:0} s.");

        string outDir = ProjectSettings.GlobalizePath("res://out/sims");
        DirAccess.MakeDirRecursiveAbsolute(outDir);
        string name = $"timetrial_{dirName}_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}";
        using (var f = FileAccess.Open($"{outDir}/{name}.md", FileAccess.ModeFlags.Write)) f?.StoreString(md.ToString());
        if (DevTools.Instance?.Arg("writetargets") != null)
        {
            string dir = ProjectSettings.GlobalizePath(TargetsDir);
            DirAccess.MakeDirRecursiveAbsolute(dir);
            // Merged into the file: a run with --car=<id> replaces only that car's targets.
            string file = $"{dir}/{dirName}.json";
            var all = FileAccess.FileExists(file)
                ? JsonSerializer.Deserialize<Dictionary<string, double[]>>(FileAccess.GetFileAsString(file)) ?? new()
                : new Dictionary<string, double[]>();
            foreach (var (key, value) in targets) all[key] = value;
            using var f = FileAccess.Open(file, FileAccess.ModeFlags.Write);
            f?.StoreString(JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[TT] targets written: {dir}/{dirName}.json");
        }
        GD.Print(md.ToString());
        GD.Print($"[TT] {(pass == total ? "OK" : "!!")} gold reachable by skilled, not by average: {pass}/{total} cars");
        GD.Print($"[TT] report: {outDir}/{name}.md");
        DevTools.QuitClean(GetTree(), 0);
    }
}
