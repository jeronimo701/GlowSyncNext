using System;
using System.Numerics;

namespace GlowSync.Capture;

/// <summary>scRGB linear RGB (1 = 80 nits) -> luminance-preserving extended Reinhard -> sRGB bytes.</summary>
internal static class HdrToneMapper
{
    public static Vector3 Map(Vector3 scRgb, double paperWhiteNits, double peakNits)
    {
        float white = (float)Math.Clamp(paperWhiteNits, 80, 500);
        float peak = (float)Math.Clamp(peakNits, white, 10000);
        static float Clean(float value) => float.IsNaN(value) || value < 0 ? 0 : Math.Min(value, 65504);
        var linear = new Vector3(Clean(scRgb.X), Clean(scRgb.Y), Clean(scRgb.Z)) * (80f / white);
        float luminance = Vector3.Dot(linear, new Vector3(0.2126f, 0.7152f, 0.0722f));
        if (luminance <= 0) return Vector3.Zero;
        float maximum = peak / white;
        float mapped = luminance * (1 + luminance / (maximum * maximum)) / (1 + luminance);
        linear *= mapped / luminance;
        static float Encode(float value)
        {
            value = Math.Clamp(value, 0, 1);
            return value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
        }
        return new(Encode(linear.X), Encode(linear.Y), Encode(linear.Z));
    }

    public static void ConvertRow(ReadOnlySpan<Half> rgba, Span<byte> bgra, double paperWhite, double peak)
    {
        if (rgba.Length % 4 != 0 || bgra.Length != rgba.Length) throw new ArgumentException("RGBA/BGRA row sizes do not match");
        for (int i = 0; i < rgba.Length; i += 4)
        {
            var color = Map(new((float)rgba[i], (float)rgba[i + 1], (float)rgba[i + 2]), paperWhite, peak);
            bgra[i] = (byte)MathF.Round(color.Z * 255);
            bgra[i + 1] = (byte)MathF.Round(color.Y * 255);
            bgra[i + 2] = (byte)MathF.Round(color.X * 255);
            bgra[i + 3] = 255;
        }
    }
}
