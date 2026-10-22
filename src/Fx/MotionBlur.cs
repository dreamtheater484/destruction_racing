using System;
using Godot;

namespace DestructionRacing.Fx;

/// <summary>
/// Motion blur (plan §8.4; Godot 4.7 has none built in): <see cref="MotionBlurEffect"/> on the venue's
/// WorldEnvironment, and this node, which sets its shutter every frame. The shutter is a fixed time (1/40 s at 100%),
/// so the blur looks the same at 60 and at 240 fps. On a camera cut (a new view, a replay camera, a jump) the
/// velocity buffer holds the whole jump for a frame, so that frame has no blur.
/// </summary>
public partial class MotionBlur : Node
{
    /// <summary>The shutter at 100%, in seconds.</summary>
    private const float FullShutter = 1f / 40f;

    private WorldEnvironment _environment = null!;
    private MotionBlurEffect _effect = null!;
    private Camera3D? _lastCamera;
    private Transform3D _lastPose;

    /// <summary>Adds the blur to a venue (not where there is no RenderingDevice: headless runs, the Compatibility renderer).</summary>
    public static void Attach(Node venue, WorldEnvironment environment)
    {
        if (RenderingServer.GetRenderingDevice() == null) return;
        venue.AddChild(new MotionBlur { _environment = environment, Name = "MotionBlur" });
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _effect = new MotionBlurEffect();
        _environment.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _effect } };
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_environment)) _environment.Compositor = null;
        _effect.Release();
    }

    public override void _Process(double delta)
    {
        float strength = Core.GameSettings.MotionBlur;
        var cam = GetViewport().GetCamera3D();
        bool cut = cam == null || cam != _lastCamera;
        if (cam != null && !cut)
        {
            var pose = cam.GlobalTransform;
            cut = pose.Origin.DistanceTo(_lastPose.Origin) > 4f
                || (-pose.Basis.Z).AngleTo(-_lastPose.Basis.Z) > Mathf.DegToRad(25f);
        }
        _lastCamera = cam;
        if (cam != null) _lastPose = cam.GlobalTransform;
        // The frame's real duration (slow motion slows the world, not the shutter).
        double real = delta / Math.Max(Engine.TimeScale, 0.01);
        float scale = cut || strength <= 0f ? 0f : Mathf.Clamp(strength * FullShutter / (float)Math.Max(real, 1e-4), 0f, 4f);
        _effect.Scale = scale;
        _effect.Enabled = scale > 0.001f;
    }
}

/// <summary>
/// The compute pass of the motion blur (assets/shaders/motion_blur.glsl), after the transparent pass: it blurs
/// each pixel of the frame along its motion from Godot's velocity buffer.
/// </summary>
public partial class MotionBlurEffect : CompositorEffect
{
    /// <summary>The shutter as a fraction of the frame (blur = the per-frame move × this), set by <see cref="MotionBlur"/>.</summary>
    public float Scale { get; set; }
    /// <summary>The longest blur, in screen UV.</summary>
    public float MaxLength { get; set; } = 0.05f;

    private static readonly StringName Context = "dr_motion_blur", ScratchName = "scratch";
    private RenderingDevice? _rd;
    private Rid _shader, _pipeline, _linear, _nearest, _set, _setBack;
    private (Rid Color, Rid Velocity, Rid Scratch) _setFor;
    private Rid _formatFor;
    private RenderingDevice.DataFormat _format;
    private bool _copyBack, _usable;
    private readonly float[] _params = new float[4];
    private readonly byte[] _push = new byte[16];

    public MotionBlurEffect()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        NeedsMotionVectors = true;
        AccessResolvedColor = true;
        RenderingServer.CallOnRenderThread(Callable.From(Init));
    }

    private void Init()
    {
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd == null) return;
        var file = GD.Load<RDShaderFile>("res://assets/shaders/motion_blur.glsl");
        _shader = _rd.ShaderCreateFromSpirV(file.GetSpirV());
        if (!_shader.IsValid) { GD.PushWarning("Motion blur: the shader didn't compile."); return; }
        _pipeline = _rd.ComputePipelineCreate(_shader);
        _linear = Sampler(RenderingDevice.SamplerFilter.Linear);
        _nearest = Sampler(RenderingDevice.SamplerFilter.Nearest);
    }

    private Rid Sampler(RenderingDevice.SamplerFilter filter)
    {
        // The wrappers of the RD objects are disposed at once: a finalizer left for the engine's shutdown can crash it.
        using var state = new RDSamplerState
        {
            MinFilter = filter, MagFilter = filter,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        };
        return _rd!.SamplerCreate(state);
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (_rd == null || !_pipeline.IsValid || Scale <= 0.001f) return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers) return;
        var size = buffers.GetInternalSize();
        if (size.X <= 0 || size.Y <= 0) return;

        // The resolved buffers (with MSAA the plain overloads give the multisampled ones).
        Rid color = buffers.GetColorLayer(0, false);
        Rid velocity = buffers.GetVelocityLayer(0, false);
        if (!color.IsValid || !velocity.IsValid) return;
        // The pass reads the frame and writes a scratch texture, which then goes over the frame. With MSAA the
        // resolved frame can be copied into; without it (the Low preset, and SMAA under a depth-of-field camera) it
        // can't be copied at all but is a storage image, so a second pass with no shutter copies the scratch over it.
        // The frame's usage is read once per frame texture (the format wrapper is garbage).
        if (_formatFor != color)
        {
            using var format = _rd.TextureGetFormat(color);
            _format = format.Format;
            _copyBack = (format.UsageBits & RenderingDevice.TextureUsageBits.CanCopyToBit) != 0;
            _usable = _copyBack || (format.UsageBits & RenderingDevice.TextureUsageBits.StorageBit) != 0;
            _formatFor = color;
        }
        if (!_usable) return;
        // The buffers drop the scratch texture when the size changes.
        if (!buffers.HasTexture(Context, ScratchName))
            buffers.CreateTexture(Context, ScratchName, _format,
                (uint)(RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit
                       | RenderingDevice.TextureUsageBits.CanCopyFromBit),
                RenderingDevice.TextureSamples.Samples1, size, 1, 1, true, false);
        Rid scratch = buffers.GetTexture(Context, ScratchName);

        // The uniform sets only change with the buffers (a resize, a new view, MSAA on or off).
        if (_setFor != (color, velocity, scratch) || !_rd.UniformSetIsValid(_set) || (!_copyBack && !_rd.UniformSetIsValid(_setBack)))
        {
            _set = UniformSet(color, velocity, scratch);
            _setBack = _copyBack ? default : UniformSet(scratch, velocity, color);
            _setFor = (color, velocity, scratch);
        }
        Dispatch(_set, size, Scale);
        if (_copyBack) _rd.TextureCopy(scratch, color, Vector3.Zero, Vector3.Zero, new Vector3(size.X, size.Y, 1), 0, 0, 0, 0);
        else Dispatch(_setBack, size, 0f);
    }

    private Rid UniformSet(Rid input, Rid velocity, Rid output)
    {
        var uniforms = new Godot.Collections.Array<RDUniform>
        {
            Uniform(RenderingDevice.UniformType.SamplerWithTexture, 0, _linear, input),
            Uniform(RenderingDevice.UniformType.SamplerWithTexture, 1, _nearest, velocity),
            Uniform(RenderingDevice.UniformType.Image, 2, output),
        };
        var set = UniformSetCacheRD.GetCache(_shader, 0, uniforms);
        foreach (var u in uniforms) u.Dispose();
        return set;
    }

    // One pass of the shader; with no shutter it copies its input unchanged.
    private void Dispatch(Rid set, Vector2I size, float scale)
    {
        (_params[0], _params[1], _params[2], _params[3]) = (size.X, size.Y, scale, MaxLength);
        Buffer.BlockCopy(_params, 0, _push, 0, 16);
        long list = _rd!.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, set, 0);
        _rd.ComputeListSetPushConstant(list, _push, (uint)_push.Length);
        _rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        _rd.ComputeListEnd();
    }

    private static RDUniform Uniform(RenderingDevice.UniformType type, int binding, params Rid[] ids)
    {
        var u = new RDUniform { UniformType = type, Binding = binding };
        foreach (var id in ids) u.AddId(id);
        return u;
    }

    /// <summary>Frees the GPU objects (when the venue goes, so nothing is left for the renderer's shutdown).</summary>
    public void Release()
    {
        if (_rd == null) return;
        var (rd, pipeline, shader, linear, nearest) = (_rd, _pipeline, _shader, _linear, _nearest);
        _rd = null;
        _pipeline = _shader = _linear = _nearest = default;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            foreach (var rid in new[] { pipeline, shader, linear, nearest })
                if (rid.IsValid) rd.FreeRid(rid);
        }));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) Release();
    }
}
