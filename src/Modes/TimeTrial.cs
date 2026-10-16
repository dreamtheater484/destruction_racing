using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using DestructionRacing.Tracks;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>
/// Time Trial records (plan §4.4): the best laps per layout, direction and car (a local leaderboard of 10), kept in
/// user://timetrial/records.json, and the ghost of the best lap of each in its own file.
/// </summary>
public sealed class TimeTrialRecords
{
    public const string Dir = "user://timetrial";
    public const int BoardSize = 10;
    private const string FilePath = Dir + "/records.json";

    /// <summary>A lap: its time, the split at the end of sectors 1 and 2 (seconds from the line) and the date.</summary>
    public sealed record Lap(double Time, double[] Splits, string Date);

    private readonly Dictionary<string, List<Lap>> _boards;

    private TimeTrialRecords(Dictionary<string, List<Lap>> boards) => _boards = boards;

    public static TimeTrialRecords Empty() => new(new Dictionary<string, List<Lap>>());

    public static string Key(SpeedwayLayout layout, bool reverse, string car) =>
        $"{(layout == SpeedwayLayout.Oval ? "oval" : "figure8")}_{(reverse ? "rev" : "fwd")}_{car}";

    public IReadOnlyList<Lap> Board(string key) => _boards.TryGetValue(key, out var b) ? b : Array.Empty<Lap>();
    public Lap? Best(string key) => Board(key).FirstOrDefault();

    /// <summary>Adds a lap to its board; returns its place (1 = a new best) or 0 when it didn't make the board.</summary>
    public int Submit(string key, Lap lap)
    {
        if (!_boards.TryGetValue(key, out var board)) _boards[key] = board = new List<Lap>();
        int place = board.Count(l => l.Time <= lap.Time) + 1;
        if (place > BoardSize) return 0;
        board.Insert(place - 1, lap);
        if (board.Count > BoardSize) board.RemoveAt(board.Count - 1);
        return place;
    }

    public static TimeTrialRecords Load()
    {
        var boards = new Dictionary<string, List<Lap>>();
        if (FileAccess.FileExists(FilePath))
        {
            try
            {
                using var f = FileAccess.Open(FilePath, FileAccess.ModeFlags.Read);
                var data = JsonSerializer.Deserialize<Dictionary<string, List<Lap>>>(f.GetAsText());
                if (data != null) boards = data;
            }
            catch (Exception e)
            {
                GD.PushWarning($"Time Trial records unreadable ({e.Message}); starting fresh.");
            }
        }
        return new TimeTrialRecords(boards);
    }

    public void Save()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Dir));
        using var f = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(_boards, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string Today() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// A recorded lap for the ghost car (plan §4.4): the car's pose, steering and wheel spin 30 times a second from the
/// line, played back against the clock of the current lap.
/// </summary>
public sealed class Ghost
{
    public readonly record struct Sample(float T, Vector3 Position, Quaternion Rotation, float Steer, float Spin0, float Spin1, float Spin2, float Spin3);

    public const float Rate = 30f;
    private const uint Magic = 0x48475244; // "DRGH"
    private const int Version = 1;

    public string Car { get; init; } = "";
    public double LapTime { get; set; }
    public List<Sample> Samples { get; } = new();

    public static string PathFor(string key) => $"{TimeTrialRecords.Dir}/{key}.ghost";

    public void Record(float t, Car car)
    {
        var xf = car.GlobalTransform;
        Samples.Add(new Sample(t, xf.Origin, xf.Basis.GetRotationQuaternion(), Mathf.DegToRad(car.SteerAngleDeg),
            car.WheelSpin(0), car.WheelSpin(1), car.WheelSpin(2), car.WheelSpin(3)));
    }

    /// <summary>Poses the ghost car at lap time <paramref name="t"/>.</summary>
    public void Apply(float t, Car ghost)
    {
        if (Samples.Count == 0) return;
        int hi = Samples.FindIndex(s => s.T >= t);
        if (hi < 0) hi = Samples.Count - 1;
        int lo = Math.Max(0, hi - 1);
        var a = Samples[lo];
        var b = Samples[hi];
        float k = b.T > a.T ? Mathf.Clamp((t - a.T) / (b.T - a.T), 0f, 1f) : 1f;
        ghost.GlobalTransform = new Transform3D(new Basis(a.Rotation.Slerp(b.Rotation, k)), a.Position.Lerp(b.Position, k));
        ghost.ReplaySteer = Mathf.Lerp(a.Steer, b.Steer, k);
        ghost.ReplaySpin[0] = Mathf.Lerp(a.Spin0, b.Spin0, k);
        ghost.ReplaySpin[1] = Mathf.Lerp(a.Spin1, b.Spin1, k);
        ghost.ReplaySpin[2] = Mathf.Lerp(a.Spin2, b.Spin2, k);
        ghost.ReplaySpin[3] = Mathf.Lerp(a.Spin3, b.Spin3, k);
        ghost.ReplaySpeed = b.T > a.T ? a.Position.DistanceTo(b.Position) / (b.T - a.T) : 0f;
    }

    public void Save(string path)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(TimeTrialRecords.Dir));
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null) return;
        f.Store32(Magic);
        f.Store32(Version);
        f.StorePascalString(Car);
        f.StoreDouble(LapTime);
        f.Store32((uint)Samples.Count);
        foreach (var s in Samples)
        {
            f.StoreFloat(s.T);
            f.StoreFloat(s.Position.X); f.StoreFloat(s.Position.Y); f.StoreFloat(s.Position.Z);
            f.StoreFloat(s.Rotation.X); f.StoreFloat(s.Rotation.Y); f.StoreFloat(s.Rotation.Z); f.StoreFloat(s.Rotation.W);
            f.StoreFloat(s.Steer);
            f.StoreFloat(s.Spin0); f.StoreFloat(s.Spin1); f.StoreFloat(s.Spin2); f.StoreFloat(s.Spin3);
        }
    }

    public static Ghost? Load(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null || f.Get32() != Magic || f.Get32() != Version) return null;
        var g = new Ghost { Car = f.GetPascalString(), LapTime = f.GetDouble() };
        uint n = f.Get32();
        if (n > 100_000) return null;
        for (int i = 0; i < n; i++)
        {
            float t = f.GetFloat();
            var p = new Vector3(f.GetFloat(), f.GetFloat(), f.GetFloat());
            var q = new Quaternion(f.GetFloat(), f.GetFloat(), f.GetFloat(), f.GetFloat()).Normalized();
            g.Samples.Add(new Sample(t, p, q, f.GetFloat(), f.GetFloat(), f.GetFloat(), f.GetFloat(), f.GetFloat()));
        }
        return g;
    }
}

/// <summary>
/// Medal targets (plan §4.4, §17: gold is reachable by the "skilled" PlayerProxy, not by "average"), per layout,
/// direction and car: the time-trial sims (scenes/test/TimeTrialSim.tscn --writetargets) write
/// assets/speedway/time_trial/&lt;layout&gt;_&lt;direction&gt;.json. Missing entries fall back to the ideal lap of the track's speed profile.
/// </summary>
public static class TimeTrialTargets
{
    /// <summary>One file per layout and direction, written by the sims.</summary>
    public const string Dir = TimeTrialSim.TargetsDir;
    private static Dictionary<string, double[]>? _table;

    public readonly record struct Medals(double Gold, double Silver, double Bronze)
    {
        /// <summary>3 gold, 2 silver, 1 bronze, 0 none.</summary>
        public int Of(double lap) => lap <= Gold ? 3 : lap <= Silver ? 2 : lap <= Bronze ? 1 : 0;
    }

    public static string MedalName(int medal) => medal switch { 3 => "GOLD", 2 => "SILVER", 1 => "BRONZE", _ => "" };

    public static Color MedalColor(int medal) => medal switch
    {
        3 => new Color(1f, 0.8f, 0.2f),
        2 => new Color(0.8f, 0.83f, 0.88f),
        1 => new Color(0.82f, 0.52f, 0.28f),
        _ => new Color(0.5f, 0.5f, 0.5f),
    };

    public static Medals For(string key, TrackPath path)
    {
        _table ??= LoadTable();
        if (_table.TryGetValue(key, out var m) && m.Length == 3) return new Medals(m[0], m[1], m[2]);
        // Fallback: the lap at the profile speeds (the AI's line at full skill), plus margins.
        double ideal = 0;
        for (float s = 0f; s < path.Length; s += 1f) ideal += 1.0 / Math.Max(path.SpeedAt(s), 5f);
        return new Medals(ideal * 1.04, ideal * 1.09, ideal * 1.16);
    }

    private static Dictionary<string, double[]> LoadTable()
    {
        var table = new Dictionary<string, double[]>();
        if (!DirAccess.DirExistsAbsolute(Dir)) return table;
        foreach (string file in DirAccess.GetFilesAt(Dir))
        {
            if (!file.EndsWith(".json")) continue;
            try
            {
                using var f = FileAccess.Open($"{Dir}/{file}", FileAccess.ModeFlags.Read);
                foreach (var (key, medals) in JsonSerializer.Deserialize<Dictionary<string, double[]>>(f.GetAsText()) ?? new())
                    table[key] = medals;
            }
            catch (Exception e)
            {
                GD.PushWarning($"Time Trial targets {file} unreadable: {e.Message}");
            }
        }
        return table;
    }

    public static string Format(double t)
    {
        if (double.IsNaN(t) || double.IsInfinity(t)) return "-:--.--";
        int m = (int)(t / 60);
        double s = t - m * 60;
        return string.Create(CultureInfo.InvariantCulture, $"{m}:{s:00.00}");
    }
}
