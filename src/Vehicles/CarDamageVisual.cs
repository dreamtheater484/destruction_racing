using System;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// Visual damage of one car, in car-local space: a dent volume (rgb = displacement in metres, a = crumple) and
/// a wear volume (r = scraped paint, g = rust, b = soot), uploaded as 3D textures that every car shader samples
/// (assets/shaders/car_deform.gdshaderinc, car_paint.gdshader). All parts dent together because they share it.
/// </summary>
public sealed class CarDamageVisual
{
    public const int NX = 32, NY = 24, NZ = 80;
    public Vector3 Min { get; }
    public Vector3 Size { get; }
    public Vector3 Resolution => new(NX, NY, NZ);
    public ImageTexture3D DentTexture { get; } = new();
    public ImageTexture3D WearTexture { get; } = new();
    /// <summary>Deepest displacement any voxel may reach (m).</summary>
    public float MaxDisplacement { get; set; } = 0.5f;
    /// <summary>Dent depth (m) at which the metal is fully crumpled.</summary>
    public float FullCrumpleDepth { get; set; } = 0.22f;

    /// <summary>Bumped whenever the dent volume changes (attachments that follow the metal re-sample then).</summary>
    public int DentVersion { get; private set; }

    private readonly float[] _dent = new float[NX * NY * NZ * 4];
    private readonly float[] _wear = new float[NX * NY * NZ * 4];
    private bool _dentDirty, _wearDirty;

    public CarDamageVisual(Aabb bounds)
    {
        Min = bounds.Position;
        Size = bounds.Size;
        DentTexture.Create(Image.Format.Rgbah, NX, NY, NZ, false, DentSlices());
        WearTexture.Create(Image.Format.Rgba8, NX, NY, NZ, false, WearSlices());
    }

    private Vector3 VoxelCentre(int i, int j, int k) =>
        Min + new Vector3((i + 0.5f) * Size.X / NX, (j + 0.5f) * Size.Y / NY, (k + 0.5f) * Size.Z / NZ);

    private void Range(Vector3 centre, float reach, out int i0, out int i1, out int j0, out int j1, out int k0, out int k1)
    {
        Vector3 a = (centre - Vector3.One * reach - Min) / Size;
        Vector3 b = (centre + Vector3.One * reach - Min) / Size;
        i0 = Math.Clamp((int)MathF.Floor(a.X * NX), 0, NX - 1);
        i1 = Math.Clamp((int)MathF.Ceiling(b.X * NX), 0, NX - 1);
        j0 = Math.Clamp((int)MathF.Floor(a.Y * NY), 0, NY - 1);
        j1 = Math.Clamp((int)MathF.Ceiling(b.Y * NY), 0, NY - 1);
        k0 = Math.Clamp((int)MathF.Floor(a.Z * NZ), 0, NZ - 1);
        k1 = Math.Clamp((int)MathF.Ceiling(b.Z * NZ), 0, NZ - 1);
    }

    private static float Falloff(float d, float radius)
    {
        float t = 1f - (d * d) / (radius * radius);
        return t <= 0f ? 0f : t * t * (2f - t);     // smooth, a little flat-bottomed
    }

    /// <summary>
    /// Pushes the metal around <paramref name="point"/> by <paramref name="push"/> (car-local metres, usually
    /// into the car along the hit), fading out over <paramref name="radius"/>. Deep dents crumple and lose paint.
    /// </summary>
    public void AddDent(Vector3 point, Vector3 push, float radius)
    {
        float depth = push.Length();
        if (depth < 1e-4f || radius < 1e-3f) return;
        Range(point, radius, out int i0, out int i1, out int j0, out int j1, out int k0, out int k1);
        for (int k = k0; k <= k1; k++)
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            float w = Falloff(VoxelCentre(i, j, k).DistanceTo(point), radius);
            if (w <= 0f) continue;
            int o = ((k * NY + j) * NX + i) * 4;
            var d = new Vector3(_dent[o], _dent[o + 1], _dent[o + 2]) + push * w;
            float len = d.Length();
            if (len > MaxDisplacement) d *= MaxDisplacement / len;
            _dent[o] = d.X;
            _dent[o + 1] = d.Y;
            _dent[o + 2] = d.Z;
            _dent[o + 3] = MathF.Min(1f, _dent[o + 3] + depth * w / FullCrumpleDepth);
            _wear[o] = MathF.Min(1f, _wear[o] + w * w * MathF.Min(1f, depth / 0.2f) * 0.45f);
        }
        _dentDirty = _wearDirty = true;
        DentVersion++;
    }

    /// <summary>Dent displacement (m) at a car-local point: trilinear and edge-clamped, like the GPU sampler.</summary>
    public Vector3 SampleDent(Vector3 p)
    {
        Vector3 g = (p - Min) / Size * Resolution - Vector3.One * 0.5f;
        int i = (int)MathF.Floor(g.X), j = (int)MathF.Floor(g.Y), k = (int)MathF.Floor(g.Z);
        Vector3 f = g - new Vector3(i, j, k);
        var sum = Vector3.Zero;
        for (int dk = 0; dk < 2; dk++)
        for (int dj = 0; dj < 2; dj++)
        for (int di = 0; di < 2; di++)
        {
            float w = (di == 0 ? 1f - f.X : f.X) * (dj == 0 ? 1f - f.Y : f.Y) * (dk == 0 ? 1f - f.Z : f.Z);
            int o = ((Math.Clamp(k + dk, 0, NZ - 1) * NY + Math.Clamp(j + dj, 0, NY - 1)) * NX + Math.Clamp(i + di, 0, NX - 1)) * 4;
            sum += w * new Vector3(_dent[o], _dent[o + 1], _dent[o + 2]);
        }
        return sum;
    }

    /// <summary>Scrapes paint off (amount 0..1) around a point; repeated scrapes go through primer to metal.</summary>
    public void AddScrape(Vector3 point, float radius, float amount) => AddWear(point, radius, 0, amount);

    public void AddSoot(Vector3 point, float radius, float amount) => AddWear(point, radius, 2, amount);

    /// <summary>Lets exposed metal rust (for old wrecks and the end of a long derby).</summary>
    public void AgeRust(float amount)
    {
        for (int o = 0; o < _wear.Length; o += 4)
            if (_wear[o] > 0.3f) _wear[o + 1] = MathF.Min(1f, _wear[o + 1] + amount * (_wear[o] - 0.3f) * 1.4f);
        _wearDirty = true;
    }

    private void AddWear(Vector3 point, float radius, int channel, float amount)
    {
        Range(point, radius, out int i0, out int i1, out int j0, out int j1, out int k0, out int k1);
        for (int k = k0; k <= k1; k++)
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            float w = Falloff(VoxelCentre(i, j, k).DistanceTo(point), radius);
            if (w <= 0f) continue;
            int o = ((k * NY + j) * NX + i) * 4 + channel;
            _wear[o] = MathF.Min(1f, _wear[o] + w * amount);
        }
        _wearDirty = true;
    }

    public void Clear()
    {
        Array.Clear(_dent);
        Array.Clear(_wear);
        _dentDirty = _wearDirty = true;
        DentVersion++;
    }

    /// <summary>Uploads changed volumes (call once per frame).</summary>
    public void Flush()
    {
        if (_dentDirty) DentTexture.Update(DentSlices());
        if (_wearDirty) WearTexture.Update(WearSlices());
        _dentDirty = _wearDirty = false;
    }

    private Godot.Collections.Array<Image> DentSlices()
    {
        var slices = new Godot.Collections.Array<Image>();
        var bytes = new byte[NX * NY * 4 * 2];
        for (int k = 0; k < NZ; k++)
        {
            int baseIndex = k * NX * NY * 4;
            for (int n = 0; n < NX * NY * 4; n++)
            {
                ushort h = BitConverter.HalfToUInt16Bits((Half)_dent[baseIndex + n]);
                bytes[n * 2] = (byte)h;
                bytes[n * 2 + 1] = (byte)(h >> 8);
            }
            slices.Add(Image.CreateFromData(NX, NY, false, Image.Format.Rgbah, bytes));
        }
        return slices;
    }

    private Godot.Collections.Array<Image> WearSlices()
    {
        var slices = new Godot.Collections.Array<Image>();
        var bytes = new byte[NX * NY * 4];
        for (int k = 0; k < NZ; k++)
        {
            int baseIndex = k * NX * NY * 4;
            for (int n = 0; n < NX * NY * 4; n++) bytes[n] = (byte)(Math.Clamp(_wear[baseIndex + n], 0f, 1f) * 255f + 0.5f);
            slices.Add(Image.CreateFromData(NX, NY, false, Image.Format.Rgba8, bytes));
        }
        return slices;
    }
}
