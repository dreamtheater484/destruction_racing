using Godot;

namespace DestructionRacing.Audio;

/// <summary>
/// The mix (plan §12): buses Music, Announcer and World (Sfx, Engines, Crowd) into Master with a limiter; ducking
/// of the music and crowd under the announcer and big crashes; a low-pass sweep on the world for slow motion.
/// The buses are created on first use, so no bus layout file is needed.
/// </summary>
public static class AudioMix
{
    public const string Music = "Music", Announcer = "Announcer", World = "World", Sfx = "Sfx", Engines = "Engines", Crowd = "Crowd";

    /// <summary>
    /// Base levels (dB) of the mix. M8 final mix, measured with <c>--mixlog</c> in a derby and a race (integrated,
    /// ~LUFS): the announcer about −17 on top, the engines −22 to −21, the crowd −26 to −25, the music −25 at the
    /// start to −19 at the finale (7 dB down under the announcer), the master about −18 with only the derby's
    /// biggest crashes reaching the limiter.
    /// </summary>
    public static float MusicDb = -1f, AnnouncerDb = -1f, SfxDb = -3f, EnginesDb = -9f, CrowdDb = -7f;
    /// <summary>The volume sliders of the options screen (0..1 on top of the mix).</summary>
    public static float MasterGain = 1f, MusicGain = 1f, EffectsGain = 1f, EnginesGain = 1f, CrowdGain = 1f, AnnouncerGain = 1f;

    private static AudioEffectLowPassFilter? _slowMoFilter;
    private static float _duckAnnouncer, _duckCrash, _slowMo;

    public static void Ensure()
    {
        if (AudioServer.GetBusIndex(World) >= 0) return;
        AddBus(Music, "Master");
        AddBus(Announcer, "Master");
        AddBus(World, "Master");
        AddBus(Sfx, World);
        AddBus(Engines, World);
        AddBus(Crowd, World);
        _slowMoFilter = new AudioEffectLowPassFilter { CutoffHz = 20500f, Resonance = 0.6f };
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(World), _slowMoFilter);
        AudioServer.SetBusEffectEnabled(AudioServer.GetBusIndex(World), 0, false);
        AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -0.6f, PreGainDb = 0f });
        Apply();
    }

    private static void AddBus(string name, string send)
    {
        int i = AudioServer.BusCount;
        AudioServer.AddBus(i);
        AudioServer.SetBusName(i, name);
        AudioServer.SetBusSend(i, send);
    }

    /// <summary>The announcer is talking (0..1): music and crowd dip under it.</summary>
    public static void DuckForAnnouncer(float amount) => _duckAnnouncer = amount;

    /// <summary>A big crash: the music dips for a moment so the crunch carries.</summary>
    public static void DuckForCrash(float amount) => _duckCrash = Mathf.Max(_duckCrash, Mathf.Clamp(amount, 0f, 1f));

    /// <summary>Slow motion amount (0..1): the world goes muffled and a little quieter.</summary>
    public static void SlowMo(float amount) => _slowMo = amount;

    /// <summary>Call every frame with real (unscaled) seconds.</summary>
    public static void Update(float realDt)
    {
        _duckCrash = Mathf.Max(0f, _duckCrash - realDt * 1.5f);
        Apply();
    }

    /// <summary>Applies changed sliders at once (the options screen; events call <see cref="Update"/> every frame).</summary>
    public static void Refresh() => Apply();

    private static float Db(float gain) => gain <= 0.001f ? -80f : 20f * Mathf.Log(gain) / Mathf.Log(10f);

    private static void Apply()
    {
        AudioServer.SetBusVolumeDb(0, Db(MasterGain));
        AudioServer.SetBusMute(0, MasterGain <= 0.001f);
        Set(Music, MusicDb + Db(MusicGain) - 7f * _duckAnnouncer - 5f * _duckCrash);
        Set(Announcer, AnnouncerDb + Db(AnnouncerGain));
        Set(Sfx, SfxDb + Db(EffectsGain));
        Set(Engines, EnginesDb + Db(EnginesGain) - 4f * _duckAnnouncer);
        Set(Crowd, CrowdDb + Db(CrowdGain) - 4f * _duckAnnouncer);
        int world = AudioServer.GetBusIndex(World);
        if (world < 0 || _slowMoFilter == null) return;
        bool on = _slowMo > 0.01f;
        AudioServer.SetBusEffectEnabled(world, 0, on);
        _slowMoFilter.CutoffHz = Mathf.Lerp(20500f, 700f, Mathf.Clamp(_slowMo, 0f, 1f));
        AudioServer.SetBusVolumeDb(world, -3f * _slowMo);
    }

    private static void Set(string bus, float db)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i >= 0) AudioServer.SetBusVolumeDb(i, db);
    }
}
