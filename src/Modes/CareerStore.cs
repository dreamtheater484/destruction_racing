using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DestructionRacing.AI;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>The player's car and colours: a model, a palette colour, a livery style and a race number.</summary>
public sealed record Garage(string Car = "kitsune", int Colour = 0, int Style = 0, int Number = 7)
{
    /// <summary>The livery with the roof colour of <paramref name="division"/>.</summary>
    public Livery Livery(int division)
    {
        var paint = PlayerColours.Colour(Colour);
        return new Livery(paint, Drivers.Contrast(paint), Style, Metallic: PlayerColours.IsChampion(Colour) ? 0.75f : 0f,
            Wear: 0.15f, Dirt: 0.25f, Number: Number, Roof: DerbyMode.RoofColor(division), Sponsor: 2);
    }
}

/// <summary>The player's colour choices: the 16 colours of the palette, plus the champion set once Division 1 is won.</summary>
public static class PlayerColours
{
    private static Color C(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    /// <summary>The champion livery set (plan §4.5): eight metallic finishes.</summary>
    public static readonly Color[] Champion =
    {
        C(212, 168, 52), C(186, 190, 196), C(150, 12, 24), C(52, 20, 88), C(232, 228, 214), C(64, 68, 74), C(16, 72, 44), C(20, 70, 190),
    };

    public static readonly string[] ChampionNames = { "GOLD", "CHROME", "CANDY RED", "MIDNIGHT", "PEARL", "GUNMETAL", "RACING GREEN", "ELECTRIC BLUE" };

    public static int Count(bool champion) => Drivers.Palette.Length + (champion ? Champion.Length : 0);
    public static bool IsChampion(int index) => index >= Drivers.Palette.Length;
    public static Color Colour(int index) =>
        index < Drivers.Palette.Length ? Drivers.Palette[Math.Max(0, index)] : Champion[Math.Min(index - Drivers.Palette.Length, Champion.Length - 1)];
}

/// <summary>A saved championship: the career, the player's garage and when it was last played.</summary>
public sealed class CareerSlot
{
    public CareerSlot(int slot, Career career, Garage garage, string updated)
    {
        Slot = slot;
        Career = career;
        Garage = garage;
        Updated = updated;
    }

    public int Slot { get; }
    public Career Career { get; }
    public Garage Garage { get; set; }
    public string Updated { get; set; }

    public string KindName => Career.Kind == ChampionshipKind.StockCar ? "STOCK CAR" : "WRECKIN'";

    /// <summary>"WRECKIN' · SEASON 2 · DIVISION 3 · ROUND 2 OF 5".</summary>
    public string Summary =>
        $"{KindName}  ·  SEASON {Career.Season}  ·  DIVISION {Career.PlayerDivision}  ·  " +
        (Career.SeasonComplete ? "SEASON OVER" : $"ROUND {Career.EventIndex + 1} OF {Career.Events.Count}") +
        (Career.Champion ? "  ·  CHAMPION" : "");
}

/// <summary>
/// The three championship save slots (<c>user://career/slotN.json</c>, plan §18 M7 "save and load"). Each file holds
/// the <see cref="Career.Save"/>, the garage and the date; a championship saves itself after every event.
/// </summary>
public static class CareerStore
{
    public static string Dir => Core.UserPaths.Of("career");
    public const int Slots = 3;

    private sealed class File
    {
        public Career.Save Career { get; set; } = new();
        public Garage Garage { get; set; } = new();
        public string Updated { get; set; } = "";
    }

    private static string PathOf(int slot) => $"{Dir}/slot{slot}.json";

    public static bool Exists(int slot) => FileAccess.FileExists(PathOf(slot));

    public static CareerSlot? Load(int slot)
    {
        if (!Exists(slot)) return null;
        try
        {
            using var f = FileAccess.Open(PathOf(slot), FileAccess.ModeFlags.Read);
            var file = JsonSerializer.Deserialize<File>(f.GetAsText());
            if (file == null) return null;
            var career = Rules.Career.Load(file.Career, RulesConfig.Standard);
            var garage = CarCatalog.Find(file.Garage.Car) == null ? file.Garage with { Car = "kitsune" } : file.Garage;
            return new CareerSlot(slot, career, garage, file.Updated);
        }
        catch (Exception e)
        {
            GD.PushWarning($"Championship slot {slot} unreadable ({e.Message}).");
            return null;
        }
    }

    public static void Save(CareerSlot slot)
    {
        slot.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Dir));
        var file = new File { Career = slot.Career.ToSave(), Garage = slot.Garage, Updated = slot.Updated };
        using (var f = FileAccess.Open(PathOf(slot.Slot), FileAccess.ModeFlags.Write))
            f?.StoreString(JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        Profile.Note(slot.Career);
    }

    public static void Delete(int slot)
    {
        if (Exists(slot)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PathOf(slot)));
    }

    /// <summary>A new championship in a slot: the player in Division 4, the AI in their starting divisions.</summary>
    public static CareerSlot New(int slot, ChampionshipKind kind, Garage garage)
    {
        int seed = (int)(Time.GetUnixTimeFromSystem() % 100000) + slot * 7;
        var career = new Career(kind, RulesConfig.Standard, Drivers.StartingDivisions(kind), seed);
        var s = new CareerSlot(slot, career, garage, "");
        Save(s);
        return s;
    }
}

/// <summary>
/// Progress across all championships (<c>user://profile.json</c>): the best division reached and whether Division 1
/// was won. It decides what Single Event, Practice and Time Trial offer (<see cref="Unlocks"/>).
/// </summary>
public sealed class Profile
{
    public static string FilePath => Core.UserPaths.Of("profile.json");
    public int BestDivision { get; set; } = 4;
    public bool Champion { get; set; }
    /// <summary>The best Total Destruction score (seconds survived plus points).</summary>
    public int TotalDestructionBest { get; set; }

    private static Profile? _current;
    public static Profile Current => _current ??= Load();

    private static Profile Load()
    {
        if (!FileAccess.FileExists(FilePath)) return new Profile();
        try
        {
            using var f = FileAccess.Open(FilePath, FileAccess.ModeFlags.Read);
            return JsonSerializer.Deserialize<Profile>(f.GetAsText()) ?? new Profile();
        }
        catch (Exception) { return new Profile(); }
    }

    /// <summary>Takes in a career's progress; saves when something new was reached.</summary>
    public static void Note(Career career)
    {
        var p = Current;
        int best = Math.Min(p.BestDivision, career.PlayerDivision);
        bool champion = p.Champion || career.Champion;
        if (best == p.BestDivision && champion == p.Champion) return;
        p.BestDivision = best;
        p.Champion = champion;
        p.Write();
    }

    public static void NoteTotalDestruction(int score)
    {
        if (score <= Current.TotalDestructionBest) return;
        Current.TotalDestructionBest = score;
        Current.Write();
    }

    private void Write()
    {
        using var f = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Is this layout, direction and light open (or everything, with the Unlock-all option)?</summary>
    public static bool IsOpen(Venue venue, bool reverse, Light light) =>
        Core.GameSettings.Current.UnlockAll || Unlocks.IsOpen(Current.BestDivision, venue, reverse, light);

    public static bool ChampionOpen => Core.GameSettings.Current.UnlockAll || Current.Champion;
}
