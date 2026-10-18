using System;
using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>One change to a car's damage volumes, kept for replays (<see cref="CarDamageVisual.Journal"/>).</summary>
public readonly record struct DamageOp(byte Kind, Vector3 Point, Vector3 Push, float Radius, float Amount);

/// <summary>
/// Visual damage of one car, in car-local space: a dent volume (rgb = displacement in metres, a = crumple) and
/// a wear volume (r = scraped paint, g = rust, b = soot), uploaded as 3D textures that every car shader samples
/// (assets/shaders/car_deform.gdshaderinc, car_paint.gdshader). All parts dent together because they share it.
/// With a <see cref="Journal"/> every change is also kept, so a replay can rebuild the volumes as they were at
/// any moment (plan §15.5: damage events re-applied deterministically).
/// </summary>
public sealed class CarDamageVisual
{
    private const byte OpDent = 0, OpWear = 1, OpRust = 4;   // OpWear + channel: 1 scrape, 2 rust, 3 soot

    /// <summary>Every change since <see cref="StartJournal"/>, in order; null = not kept (sims, tools).</summary>
    public List<DamageOp>? Journal { get; private set; }
    /// <summary>How many journal entries the volumes show: all of them, unless a replay went back in time.</summary>
    public int Shown { get; private set; }

    public void StartJournal() => Journal ??= new List<DamageOp>();

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

    /// <summary>
    /// Cars that may upload their volumes in one frame (plan §6.2: amortised). A car that has waited
    /// <see cref="MaxUploadDelay"/> frames uploads anyway, so a busy pileup never leaves a dent behind for long.
    /// </summary>
    public const int UploadsPerFrame = 4, MaxUploadDelay = 3;

    private readonly float[] _dent = new float[NX * NY * NZ * 4];
    private readonly float[] _wear = new float[NX * NY * NZ * 4];
    // One image per Z slice, kept between uploads; only the slices a change touched are re-encoded.
    private readonly Godot.Collections.Array<Image> _dentImages = new(), _wearImages = new();
    private readonly byte[] _dentBytes = new byte[NX * NY * 4 * 2], _wearBytes = new byte[NX * NY * 4];
    private int _dentK0 = NZ, _dentK1 = -1, _wearK0 = NZ, _wearK1 = -1;
    private bool _dentDirty, _wearDirty;
    private ulong _dirtySince;
    private static ulong _uploadFrame;
    private static int _uploadsThisFrame;

    public CarDamageVisual(Aabb bounds)
    {
        Min = bounds.Position;
        Size = bounds.Size;
        for (int k = 0; k < NZ; k++)
        {
            _dentImages.Add(Image.CreateFromData(NX, NY, false, Image.Format.Rgbah, _dentBytes));
            _wearImages.Add(Image.CreateFromData(NX, NY, false, Image.Format.Rgba8, _wearBytes));
        }
        DentTexture.Create(Image.Format.Rgbah, NX, NY, NZ, false, _dentImages);
        WearTexture.Create(Image.Format.Rgba8, NX, NY, NZ, false, _wearImages);
    }

    private void MarkDent(int k0, int k1)
    {
        _dentK0 = Math.Min(_dentK0, k0);
        _dentK1 = Math.Max(_dentK1, k1);
        MarkDirty();
        _dentDirty = true;
    }

    private void MarkWear(int k0, int k1)
    {
        _wearK0 = Math.Min(_wearK0, k0);
        _wearK1 = Math.Max(_wearK1, k1);
        MarkDirty();
        _wearDirty = true;
    }

    private void MarkDirty()
    {
        if (!_dentDirty && !_wearDirty) _dirtySince = Engine.GetProcessFrames();
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
        if (Record(new DamageOp(OpDent, point, push, radius, 0f))) DoDent(point, push, radius);
    }

    /// <summary>
    /// Keeps a change in the journal. False while a replay shows an earlier moment: nothing new may land on the
    /// past (the cars don't take damage in a replay; this guards tools and stray calls).
    /// </summary>
    private bool Record(DamageOp op)
    {
        if (Journal == null) return true;
        if (Shown < Journal.Count) return false;
        Journal.Add(op);
        Shown = Journal.Count;
        return true;
    }

    /// <summary>
    /// Shows the volumes as they were after the first <paramref name="count"/> journal entries (replays): going
    /// back clears them and re-applies from the start, going forward applies only the new entries.
    /// </summary>
    public void ReplayTo(int count)
    {
        if (Journal == null) return;
        count = Math.Clamp(count, 0, Journal.Count);
        if (count == Shown) return;
        if (count < Shown)
        {
            ClearVolumes();
            Shown = 0;
        }
        for (int n = Shown; n < count; n++)
        {
            var op = Journal[n];
            if (op.Kind == OpDent) DoDent(op.Point, op.Push, op.Radius);
            else if (op.Kind == OpRust) DoRust(op.Amount);
            else DoWear(op.Point, op.Radius, op.Kind - OpWear, op.Amount);
        }
        Shown = count;
    }

    /// <summary>Time in the dents and the volume uploads, all cars (the deformation budget, plan §16), in Stopwatch ticks.</summary>
    public static long Ticks;

    private void DoDent(Vector3 point, Vector3 push, float radius)
    {
        float depth = push.Length();
        if (depth < 1e-4f || radius < 1e-3f) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        DoDentVoxels(point, push, radius, depth);
        Ticks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
    }

    private void DoDentVoxels(Vector3 point, Vector3 push, float radius, float depth)
    {
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
        MarkDent(k0, k1);
        MarkWear(k0, k1);
        DentVersion++;
    }

    /// <summary>A copy of the dent volume as it is now (for parts that come off).</summary>
    public ImageTexture3D SnapshotDentTexture()
    {
        EncodeDent();
        var t = new ImageTexture3D();
        t.Create(Image.Format.Rgbah, NX, NY, NZ, false, _dentImages);
        return t;
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
        if (Record(new DamageOp(OpRust, Vector3.Zero, Vector3.Zero, 0f, amount))) DoRust(amount);
    }

    private void AddWear(Vector3 point, float radius, int channel, float amount)
    {
        if (Record(new DamageOp((byte)(OpWear + channel), point, Vector3.Zero, radius, amount))) DoWear(point, radius, channel, amount);
    }

    private void DoRust(float amount)
    {
        for (int o = 0; o < _wear.Length; o += 4)
            if (_wear[o] > 0.3f) _wear[o + 1] = MathF.Min(1f, _wear[o + 1] + amount * (_wear[o] - 0.3f) * 1.4f);
        MarkWear(0, NZ - 1);
    }

    private void DoWear(Vector3 point, float radius, int channel, float amount)
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
        MarkWear(k0, k1);
    }

    /// <summary>A fingerprint of both volumes (the replay self-test: rebuilt from the journal they must match).</summary>
    public ulong Checksum()
    {
        ulong h = 14695981039346656037UL;
        foreach (float f in _dent) h = (h ^ BitConverter.SingleToUInt32Bits(f)) * 1099511628211UL;
        foreach (float f in _wear) h = (h ^ BitConverter.SingleToUInt32Bits(f)) * 1099511628211UL;
        return h;
    }

    /// <summary>A pristine car (tools). With a journal it starts over too.</summary>
    public void Clear()
    {
        Journal?.Clear();
        Shown = 0;
        ClearVolumes();
    }

    private void ClearVolumes()
    {
        Array.Clear(_dent);
        Array.Clear(_wear);
        MarkDent(0, NZ - 1);
        MarkWear(0, NZ - 1);
        DentVersion++;
    }

    /// <summary>
    /// Uploads changed volumes; call once per frame. <paramref name="budgeted"/> shares
    /// <see cref="UploadsPerFrame"/> between all cars (the in-game path); stills and tools upload at once.
    /// </summary>
    public void Flush(bool budgeted = false)
    {
        if (!_dentDirty && !_wearDirty) return;
        if (budgeted)
        {
            ulong frame = Engine.GetProcessFrames();
            if (frame != _uploadFrame) { _uploadFrame = frame; _uploadsThisFrame = 0; }
            if (_uploadsThisFrame >= UploadsPerFrame && frame - _dirtySince < MaxUploadDelay) return;
            _uploadsThisFrame++;
        }
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_dentDirty) { EncodeDent(); DentTexture.Update(_dentImages); }
        if (_wearDirty) { EncodeWear(); WearTexture.Update(_wearImages); }
        Ticks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
        _dentDirty = _wearDirty = false;
    }

    private void EncodeDent()
    {
        for (int k = _dentK0; k <= _dentK1; k++)
        {
            int baseIndex = k * NX * NY * 4;
            for (int n = 0; n < NX * NY * 4; n++)
            {
                ushort h = BitConverter.HalfToUInt16Bits((Half)_dent[baseIndex + n]);
                _dentBytes[n * 2] = (byte)h;
                _dentBytes[n * 2 + 1] = (byte)(h >> 8);
            }
            _dentImages[k].SetData(NX, NY, false, Image.Format.Rgbah, _dentBytes);
        }
        _dentK0 = NZ;
        _dentK1 = -1;
    }

    private void EncodeWear()
    {
        for (int k = _wearK0; k <= _wearK1; k++)
        {
            int baseIndex = k * NX * NY * 4;
            for (int n = 0; n < NX * NY * 4; n++) _wearBytes[n] = (byte)(Math.Clamp(_wear[baseIndex + n], 0f, 1f) * 255f + 0.5f);
            _wearImages[k].SetData(NX, NY, false, Image.Format.Rgba8, _wearBytes);
        }
        _wearK0 = NZ;
        _wearK1 = -1;
    }
}
