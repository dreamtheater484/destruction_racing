using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Fx;

/// <summary>
/// Reflection probes over a venue (post-M8 car look). Without them the cars' paint, chrome and glass reflect only
/// the sky picture, whose ground is the photo's own hills: never the track, the walls, the stands or, at night,
/// the floodlights. Each probe is captured once when the venue loads, without the cars (they are on
/// <see cref="Vehicles.CarModel.RenderLayer"/>); the sky reflection takes over outside them. Not headless.
/// </summary>
public static class VenueReflections
{
    /// <param name="centres">The probes' box centres (about car-roof height over the ground).</param>
    /// <param name="size">Each probe's box.</param>
    /// <param name="boxProjection">For an enclosed arena: reflections parallax-corrected against the box.</param>
    /// <param name="captureOffset">Where each probe captures from, relative to its box's centre.</param>
    public static void Add(Node venue, IEnumerable<Vector3> centres, Vector3 size, float blend, bool boxProjection = false,
                           Vector3 captureOffset = default, float maxDistance = 900f)
    {
        if (RenderingServer.GetRenderingDevice() == null) return;
        int i = 0;
        foreach (var c in centres)
            venue.AddChild(new ReflectionProbe
            {
                Name = $"Reflections{i++}",
                Position = c,
                Size = size,
                OriginOffset = captureOffset,
                BlendDistance = blend,
                BoxProjection = boxProjection,
                Interior = false,
                EnableShadows = true,
                MaxDistance = maxDistance,
                CullMask = 0xFFFFFu & ~Vehicles.CarModel.RenderLayer,
                UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
                AmbientMode = ReflectionProbe.AmbientModeEnum.Disabled,
            });
    }

    /// <summary>
    /// Capture points evenly along closed lines, about <paramref name="spacing"/> metres apart (each line's length
    /// divided evenly, so no gap opens where it closes), merged where lines share a stretch.
    /// </summary>
    public static List<Vector3> AlongLines(IEnumerable<IReadOnlyList<Vector3>> lines, float spacing)
    {
        var points = new List<Vector3>();
        foreach (var line in lines)
        {
            float length = 0f;
            for (int k = 0; k < line.Count; k++) length += line[k].DistanceTo(line[(k + 1) % line.Count]);
            float step = length / Mathf.Max(1, Mathf.CeilToInt(length / spacing));
            float run = 0f;
            for (int k = 0; k < line.Count; k++)
            {
                Vector3 a = line[k], b = line[(k + 1) % line.Count];
                float len = a.DistanceTo(b);
                while (run < len)
                {
                    Vector3 p = a.Lerp(b, run / len);
                    if (!points.Exists(q => q.DistanceTo(p) < spacing * 0.6f)) points.Add(p);
                    run += step;
                }
                run -= len;
            }
        }
        return points;
    }
}
