using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Fx;

/// <summary>A car part that came off: a small rigid body the cars can knock around (not a hit for scoring).</summary>
public partial class DebrisPiece : RigidBody3D
{
    public string Kind { get; set; } = "";
    public double Born { get; set; }
    public float SlowTime { get; set; }
}

/// <summary>
/// Physics debris of one scene (plan §8.3): bumpers, hoods, doors, mirrors and wheels that came off. At most
/// <see cref="MaxPieces"/> live at once (the oldest go first); a piece at rest for a moment freezes into a static
/// body that only the world collides with, so piles of parts cost nothing.
/// </summary>
public partial class DebrisPool : Node3D
{
    /// <summary>Moving debris: collides with the world, cars and other debris.</summary>
    public const uint DebrisLayer = 4;
    /// <summary>Debris at rest: lies on the floor, cars pass over it.</summary>
    public const uint RestingLayer = 8;
    public const uint WorldLayer = 1, CarLayer = 2;

    public int MaxPieces { get; set; } = 150;
    public int Count => _pieces.Count;

    private readonly List<DebrisPiece> _pieces = new();
    private double _time;

    public static DebrisPool Get(Node from)
    {
        var tree = from.GetTree();
        Node scene = tree.CurrentScene ?? tree.Root;
        if (scene.GetNodeOrNull<DebrisPool>("DebrisPool") is { } pool) return pool;
        pool = new DebrisPool { Name = "DebrisPool" };
        scene.AddChild(pool);
        return pool;
    }

    /// <summary>
    /// Turns <paramref name="visuals"/> (kept where they are in the world) into one rigid piece with a box
    /// collider of <paramref name="size"/> centred at <paramref name="centre"/> (world space, oriented by <paramref name="basis"/>).
    /// </summary>
    public DebrisPiece Spawn(string kind, IEnumerable<Node3D> visuals, Vector3 centre, Basis basis, Shape3D shape,
        float mass, Vector3 velocity, Vector3 angularVelocity)
    {
        while (_pieces.Count >= MaxPieces)
        {
            _pieces[0].QueueFree();
            _pieces.RemoveAt(0);
        }
        var piece = new DebrisPiece
        {
            Kind = kind,
            Born = _time,
            Mass = mass,
            CollisionLayer = DebrisLayer,
            CollisionMask = WorldLayer | CarLayer | DebrisLayer,
            ContinuousCd = true,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.7f, Bounce = 0.15f },
            AngularDamp = 0.4f,
            LinearDamp = 0.05f,
        };
        AddChild(piece);
        piece.GlobalTransform = new Transform3D(basis, centre);
        piece.AddChild(new CollisionShape3D { Shape = shape });
        foreach (var v in visuals) v.Reparent(piece, true);
        piece.LinearVelocity = velocity;
        piece.AngularVelocity = angularVelocity;
        _pieces.Add(piece);
        return piece;
    }

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        float dt = (float)delta;
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            var p = _pieces[i];
            if (!IsInstanceValid(p)) { _pieces.RemoveAt(i); continue; }
            if (p.Freeze) continue;
            if (p.LinearVelocity.LengthSquared() < 0.09f && p.AngularVelocity.LengthSquared() < 0.25f && _time - p.Born > 1.0)
                p.SlowTime += dt;
            else
                p.SlowTime = 0f;
            if (p.SlowTime > 1.5f || p.GlobalPosition.Y < -20f)
            {
                p.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
                p.Freeze = true;
                p.CollisionLayer = RestingLayer;
                p.CollisionMask = WorldLayer;
            }
        }
    }
}
