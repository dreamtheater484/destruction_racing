using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// Night headlights (plan §8.1): every car's lamps glow; real spot lights and beam cones go to the camera's car
/// and the cars nearest the camera (6 in all), re-assigned a few times a second. Broken headlamps dim the beam,
/// a wrecked car's lights go out.
/// </summary>
public partial class HeadlightRig : Node3D
{
    public const int Slots = 6;
    private const float BeamLength = 16f;

    /// <summary>Spot lights and beams (night); without them only the lamp lenses glow (dusk).</summary>
    public bool Beams { get; set; } = true;
    public IReadOnlyList<Car> Cars { get; set; } = new List<Car>();
    /// <summary>The car the camera follows: always gets a slot.</summary>
    public System.Func<Car?> Focus { get; set; } = () => null;

    private readonly SpotLight3D[] _spots = new SpotLight3D[Slots];
    private readonly MeshInstance3D[] _cones = new MeshInstance3D[Slots];
    private readonly Car?[] _assigned = new Car?[Slots];
    private float _reassign;

    public override void _Ready()
    {
        foreach (var car in Cars)
            if (car.Visual != null) car.Visual.LightsOn = true;
        if (!Beams) return;
        var coneMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/light_cone.gdshader") };
        var cone = new CylinderMesh
        {
            TopRadius = 0.12f,
            BottomRadius = 3.4f,
            Height = BeamLength,
            RadialSegments = 20,
            Rings = 1,
            CapTop = false,
            CapBottom = false,
            Material = coneMaterial,
        };
        for (int i = 0; i < Slots; i++)
        {
            _spots[i] = new SpotLight3D
            {
                Name = $"Headlight{i}",
                LightColor = new Color(1f, 0.9f, 0.74f),
                LightEnergy = 0f,
                SpotRange = 30f,
                SpotAngle = 30f,
                SpotAngleAttenuation = 1.6f,
                SpotAttenuation = 1.1f,
                LightVolumetricFogEnergy = 0f,     // moving lights: no fog ghosting (plan §8.1)
                ShadowEnabled = false,
                Visible = false,
            };
            AddChild(_spots[i]);
            _cones[i] = new MeshInstance3D
            {
                Name = $"Beam{i}",
                Mesh = cone,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Visible = false,
            };
            AddChild(_cones[i]);
        }
    }

    public override void _Process(double delta)
    {
        if (!Beams) return;
        _reassign -= (float)delta;
        if (_reassign <= 0f)
        {
            _reassign = 0.25f;
            Assign();
        }
        for (int i = 0; i < Slots; i++)
        {
            var car = _assigned[i];
            float strength = car is { Visual: { } v } && IsInstanceValid(car) ? v.HeadlightStrength : 0f;
            bool on = strength > 0f;
            _spots[i].Visible = on;
            _cones[i].Visible = on;
            if (!on) continue;
            var xf = car!.GlobalTransform;
            var origin = xf * car.Visual!.HeadlightOrigin;
            // Aimed along the car, dipped a little.
            var fwd = (-xf.Basis.Z - xf.Basis.Y * 0.07f).Normalized();
            var up = xf.Basis.Y;
            _spots[i].GlobalTransform = new Transform3D(Basis.LookingAt(fwd, up), origin);
            _spots[i].LightEnergy = 5f * strength;
            // The cone's narrow top (+Y) at the lamp, its wide end BeamLength ahead.
            var coneBasis = new Basis(xf.Basis.X, -fwd, xf.Basis.X.Cross(-fwd)).Orthonormalized();
            _cones[i].GlobalTransform = new Transform3D(coneBasis, origin + fwd * (BeamLength * 0.5f));
            _cones[i].SetInstanceShaderParameter("strength", strength);
        }
    }

    private void Assign()
    {
        var camera = GetViewport().GetCamera3D();
        var from = camera?.GlobalPosition ?? Vector3.Zero;
        var focus = Focus();
        var lit = Cars.Where(c => c.Visual is { HeadlightStrength: > 0f })
            .OrderBy(c => c == focus ? -1f : c.GlobalPosition.DistanceSquaredTo(from))
            .Take(Slots)
            .ToList();
        // Keep cars in the slot they already have, so the lights don't jump between slots.
        var free = new Queue<int>();
        for (int i = 0; i < Slots; i++)
        {
            if (_assigned[i] is { } c && lit.Remove(c)) continue;
            _assigned[i] = null;
            free.Enqueue(i);
        }
        foreach (var c in lit)
            if (free.Count > 0) _assigned[free.Dequeue()] = c;
    }
}
