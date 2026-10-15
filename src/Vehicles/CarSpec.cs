using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// The physical layout of a generated car, read once from its glTF (model space: ground at y = 0, X right,
/// -Z forward): the body box, wheel centres and size, and the bounds of every named part. The vehicle sim
/// sizes its collider, axles and track from it so the physics matches what you see.
/// </summary>
public sealed class CarSpec
{
    public string Id { get; private init; } = "";
    public Aabb Body { get; private init; }
    /// <summary>Wheel centres in model space, in <see cref="Rules.Wheel"/> order (FL, FR, RL, RR).</summary>
    public Vector3[] WheelCentres { get; private init; } = new Vector3[4];
    public float WheelRadius { get; private init; }
    public float WheelWidth { get; private init; }
    public IReadOnlyDictionary<string, Aabb> Parts => _parts;

    private readonly Dictionary<string, Aabb> _parts = new();
    private static readonly Dictionary<string, CarSpec> Cache = new();
    private static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };

    public float HalfTrack => (Mathf.Abs(WheelCentres[0].X) + Mathf.Abs(WheelCentres[1].X) + Mathf.Abs(WheelCentres[2].X) + Mathf.Abs(WheelCentres[3].X)) * 0.25f;
    public float FrontAxleZ => 0.5f * (WheelCentres[0].Z + WheelCentres[1].Z);
    public float RearAxleZ => 0.5f * (WheelCentres[2].Z + WheelCentres[3].Z);
    public float WheelCentreY => WheelCentres[0].Y;

    /// <summary>Half extents for the damage zones (about the car origin, as the rules expect).</summary>
    public Vector3 ZoneHalfExtents => new(
        Mathf.Max(Mathf.Abs(Body.Position.X), Mathf.Abs(Body.End.X)),
        Body.Size.Y * 0.5f,
        Mathf.Max(Mathf.Abs(Body.Position.Z), Mathf.Abs(Body.End.Z)));

    public bool TryPart(string name, out Aabb box) => _parts.TryGetValue(name, out box);

    public static CarSpec For(string id)
    {
        if (Cache.TryGetValue(id, out var spec)) return spec;
        var root = GD.Load<PackedScene>($"res://assets/cars/{id}/{id}.glb").Instantiate<Node3D>();
        var parts = new Dictionary<string, Aabb>();
        Collect(root, Transform3D.Identity, parts);
        var wheels = new Vector3[4];
        float radius = 0.3f, width = 0.2f;
        for (int i = 0; i < 4; i++)
        {
            var node = root.FindChild(WheelNames[i], true, false) as Node3D;
            wheels[i] = node?.Position ?? new Vector3(i % 2 == 0 ? -0.75f : 0.75f, 0.3f, i < 2 ? -1.3f : 1.3f);
        }
        if (parts.TryGetValue("Wheel_FL_Tyre", out var tyre))
        {
            radius = tyre.Size.Y * 0.5f;
            width = tyre.Size.X;
        }
        spec = new CarSpec
        {
            Id = id,
            Body = parts.TryGetValue("Body", out var body) ? body : new Aabb(new Vector3(-0.9f, 0.2f, -2.3f), new Vector3(1.8f, 1.1f, 4.6f)),
            WheelCentres = wheels,
            WheelRadius = radius,
            WheelWidth = width,
        };
        foreach (var (k, v) in parts) spec._parts[k] = v;
        root.Free();
        Cache[id] = spec;
        return spec;
    }

    private static void Collect(Node node, Transform3D parent, Dictionary<string, Aabb> parts)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is not Node3D n3) continue;
            var xf = parent * n3.Transform;
            if (n3 is MeshInstance3D mi && mi.Mesh != null) parts[mi.Name] = xf * mi.Mesh.GetAabb();
            Collect(n3, xf, parts);
        }
    }
}
