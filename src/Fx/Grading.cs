using System;
using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Fx;

/// <summary>
/// The colour grade per light (plan §8.4: "a colour-grading LUT per preset"): a 32³ 3D LUT built at load time and
/// set as the venue environment's colour correction, on top of AgX. The grades are deliberately light: a gentle
/// S-curve and a touch more colour everywhere; cool shadows and warm highlights in the afternoon; warm gold at
/// golden hour; blue shadows under sodium-warm floodlights at night. The FILM LOOK option switches it (and the
/// grain and vignette of <see cref="UI.ScreenFx"/>) off.
/// </summary>
public static class Grading
{
    public enum Light { Afternoon, GoldenHour, Night }

    private const int N = 32;
    private static readonly Dictionary<Light, ImageTexture3D> Luts = new();

    private readonly record struct Grade(Vector3 Lift, Vector3 Gain, float Curve, float Saturation);

    private static Grade For(Light light) => light switch
    {
        // The golden-hour scene is warm already: less warmth there. The night is mostly shadow, which an S-curve would
        // only crush: no curve at night, just the cool shadows and the warm lights.
        Light.GoldenHour => new Grade(new Vector3(0.000f, 0.003f, 0.020f), new Vector3(1.015f, 1.0f, 0.965f), 0.08f, 1.03f),
        Light.Night => new Grade(new Vector3(0.002f, 0.008f, 0.030f), new Vector3(1.02f, 1.0f, 0.97f), 0.0f, 1.04f),
        _ => new Grade(new Vector3(-0.004f, 0.0f, 0.012f), new Vector3(1.012f, 1.0f, 0.985f), 0.07f, 1.05f),
    };

    /// <summary>The light of a venue look scene, from its file name (…_Golden.tscn, …_Night.tscn).</summary>
    public static Light FromScene(string path) =>
        path.Contains("Night") ? Light.Night : path.Contains("Golden") ? Light.GoldenHour : Light.Afternoon;

    /// <summary>Grades the environment (and remembers it has a grade, so the FILM LOOK option can switch it).</summary>
    public static void Apply(Godot.Environment env, Light light)
    {
        env.AdjustmentColorCorrection = Lut(light);
        env.AdjustmentBrightness = 1f;
        env.AdjustmentContrast = 1f;
        env.AdjustmentSaturation = 1f;
        env.SetMeta("dr_grade", true);
        env.AdjustmentEnabled = Core.GameSettings.FilmLook;
    }

    public static ImageTexture3D Lut(Light light)
    {
        if (Luts.TryGetValue(light, out var lut)) return lut;
        var g = For(light);
        var images = new Godot.Collections.Array<Image>();
        var data = new byte[N * N * 3];
        for (int b = 0; b < N; b++)
        {
            for (int gi = 0; gi < N; gi++)
            for (int r = 0; r < N; r++)
            {
                // Each texel holds the grade of the colour at its centre (the sampler interpolates between centres).
                var c = new Vector3((r + 0.5f) / N, (gi + 0.5f) / N, (b + 0.5f) / N);
                var o = Map(c, g);
                int i = (gi * N + r) * 3;
                data[i] = (byte)Math.Clamp((int)MathF.Round(o.X * 255f), 0, 255);
                data[i + 1] = (byte)Math.Clamp((int)MathF.Round(o.Y * 255f), 0, 255);
                data[i + 2] = (byte)Math.Clamp((int)MathF.Round(o.Z * 255f), 0, 255);
            }
            images.Add(Image.CreateFromData(N, N, false, Image.Format.Rgb8, data));
        }
        lut = new ImageTexture3D();
        lut.Create(Image.Format.Rgb8, N, N, N, false, images);
        Luts[light] = lut;
        return lut;
    }

    /// <summary>The grade of one display-space colour (0..1).</summary>
    private static Vector3 Map(Vector3 c, Grade g)
    {
        // Lift the shadows and scale the highlights per channel (white balance).
        c = new Vector3(c.X * g.Gain.X + g.Lift.X * (1f - c.X), c.Y * g.Gain.Y + g.Lift.Y * (1f - c.Y), c.Z * g.Gain.Z + g.Lift.Z * (1f - c.Z));
        // A gentle S-curve on each channel.
        c = new Vector3(Curve(c.X, g.Curve), Curve(c.Y, g.Curve), Curve(c.Z, g.Curve));
        // Saturation around the luminance.
        float lum = c.Dot(new Vector3(0.2126f, 0.7152f, 0.0722f));
        c = new Vector3(lum, lum, lum) + (c - new Vector3(lum, lum, lum)) * g.Saturation;
        return c.Clamp(Vector3.Zero, Vector3.One);
    }

    private static float Curve(float x, float k)
    {
        x = Math.Clamp(x, 0f, 1f);
        float s = x * x * (3f - 2f * x);
        return x + (s - x) * k;
    }
}
