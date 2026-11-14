using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;

namespace SpaceSim.Procedural
{
    /// <summary>
    /// A colour map of a whole body in equirectangular projection (a "planet texture" drawn by the artists, e.g. in
    /// Blender): x = longitude −180°…+180° (the centre column is longitude 0), y = latitude, row 0 = south pole.
    /// Pixels are kept as sRGB values 0…1 — the colour space of all surface colours (the surface shader decodes sRGB → linear
    /// itself), so the map mixes with the generator's own colours and looks on screen as it does in the image.
    ///
    /// Besides the colour it offers a heavily smoothed brightness field (<see cref="SmoothBrightness"/>): dark,
    /// large-scale regions are lowland basins ("seas"), so the generator can lower them. Small features such as crater
    /// rims are deliberately blurred away — their brightness says nothing reliable about height.
    /// Engine-independent: Runtime decodes the image and registers it in <see cref="SurfaceAlbedoMaps"/>.
    /// </summary>
    public sealed class SurfaceAlbedoMap
    {
        readonly float[] rgb;      // sRGB 0…1, 3 floats per pixel
        readonly float[] smooth;   // smoothed luminance / mean luminance − 1 (0 = average brightness)

        public int Width { get; }
        public int Height { get; }
        public float MeanLuminance { get; }

        /// <summary>
        /// Builds a map from 8-bit sRGB pixels, 4 bytes per pixel (RGBA, alpha ignored), rows from bottom (south) to top
        /// (north) — the order of Unity's Texture2D.GetPixels32.
        /// </summary>
        public static SurfaceAlbedoMap FromSrgb32(int width, int height, byte[] rgba, int smoothingRadiusPixels = 0)
        {
            if (width < 2 || height < 2) throw new ArgumentException("map is too small");
            if (rgba == null || rgba.Length < width * height * 4) throw new ArgumentException("pixel data is shorter than width × height × 4");
            var lut = new float[256];
            for (int i = 0; i < 256; i++) lut[i] = i / 255f;
            var rgb = new float[width * height * 3];
            for (int p = 0; p < width * height; p++)
            {
                rgb[p * 3 + 0] = lut[rgba[p * 4 + 0]];
                rgb[p * 3 + 1] = lut[rgba[p * 4 + 1]];
                rgb[p * 3 + 2] = lut[rgba[p * 4 + 2]];
            }
            return new SurfaceAlbedoMap(width, height, rgb, smoothingRadiusPixels);
        }

        /// <summary>Builds a map from surface colours (sRGB 0…1, like PlanetGenerationSettings colours), rows from south to north.</summary>
        public static SurfaceAlbedoMap FromColors(int width, int height, ColorRGB[] pixels, int smoothingRadiusPixels = 0)
        {
            if (width < 2 || height < 2) throw new ArgumentException("map is too small");
            if (pixels == null || pixels.Length < width * height) throw new ArgumentException("pixel data is shorter than width × height");
            var rgb = new float[width * height * 3];
            for (int p = 0; p < width * height; p++)
            {
                rgb[p * 3 + 0] = pixels[p].r;
                rgb[p * 3 + 1] = pixels[p].g;
                rgb[p * 3 + 2] = pixels[p].b;
            }
            return new SurfaceAlbedoMap(width, height, rgb, smoothingRadiusPixels);
        }

        SurfaceAlbedoMap(int width, int height, float[] linearRgb, int smoothingRadiusPixels)
        {
            Width = width;
            Height = height;
            rgb = linearRgb;

            var lum = new float[width * height];
            double sum = 0.0;
            for (int p = 0; p < lum.Length; p++)
            {
                lum[p] = 0.2126f * rgb[p * 3] + 0.7152f * rgb[p * 3 + 1] + 0.0722f * rgb[p * 3 + 2];
                sum += lum[p];
            }
            MeanLuminance = (float)Math.Max(1e-6, sum / lum.Length);

            // ~1/40 of the width by default: on a 1280-px map that is a 32-px radius (≈ 50 km on a 430-km moon).
            int r = smoothingRadiusPixels > 0 ? smoothingRadiusPixels : Math.Max(1, width / 40);
            float[] blurred = lum;
            for (int pass = 0; pass < 3; pass++) blurred = BoxBlur(blurred, width, height, r); // 3 box passes ≈ Gaussian
            smooth = new float[blurred.Length];
            float maxAbs = 0f;
            for (int p = 0; p < blurred.Length; p++)
            {
                smooth[p] = blurred[p] / MeanLuminance - 1f;
                maxAbs = Math.Max(maxAbs, Math.Abs(smooth[p]));
            }
            MaxAbsSmoothBrightness = maxAbs;
        }

        /// <summary>Largest |<see cref="SmoothBrightness"/>| anywhere on the map (bounds the relief it can add).</summary>
        public float MaxAbsSmoothBrightness { get; }

        /// <summary>Surface colour (sRGB) at a planet-fixed unit direction (bilinear, wraps in longitude).</summary>
        public ColorRGB Sample(Vector3d unitDirection)
        {
            Locate(unitDirection, out int x0, out int y0, out int x1, out int y1, out float fx, out float fy);
            int a = (y0 * Width + x0) * 3, b = (y0 * Width + x1) * 3, c = (y1 * Width + x0) * 3, d = (y1 * Width + x1) * 3;
            float w00 = (1f - fx) * (1f - fy), w10 = fx * (1f - fy), w01 = (1f - fx) * fy, w11 = fx * fy;
            return new ColorRGB(
                rgb[a] * w00 + rgb[b] * w10 + rgb[c] * w01 + rgb[d] * w11,
                rgb[a + 1] * w00 + rgb[b + 1] * w10 + rgb[c + 1] * w01 + rgb[d + 1] * w11,
                rgb[a + 2] * w00 + rgb[b + 2] * w10 + rgb[c + 2] * w01 + rgb[d + 2] * w11);
        }

        /// <summary>
        /// Large-scale brightness relative to the map's mean: 0 = average, −0.3 = a region 30 % darker than average
        /// (a "sea"), +0.2 = bright highlands.
        /// </summary>
        public double SmoothBrightness(Vector3d unitDirection)
        {
            Locate(unitDirection, out int x0, out int y0, out int x1, out int y1, out float fx, out float fy);
            float top = smooth[y0 * Width + x0] * (1f - fx) + smooth[y0 * Width + x1] * fx;
            float bottom = smooth[y1 * Width + x0] * (1f - fx) + smooth[y1 * Width + x1] * fx;
            return top * (1f - fy) + bottom * fy;
        }

        void Locate(Vector3d n, out int x0, out int y0, out int x1, out int y1, out float fx, out float fy)
        {
            double len = n.Magnitude;
            double z = len > 0.0 ? MathD.Clamp(n.z / len, -1.0, 1.0) : 0.0;
            double lat = Math.Asin(z);
            double lon = Math.Atan2(n.y, n.x);
            // Pixel centres: column i covers longitudes around −π + (i + 0.5)·2π/W.
            double u = (lon + Math.PI) / (2.0 * Math.PI) * Width - 0.5;
            double v = (lat + 0.5 * Math.PI) / Math.PI * Height - 0.5;
            double fu = Math.Floor(u);
            fx = (float)(u - fu);
            x0 = Wrap((int)fu, Width);
            x1 = Wrap(x0 + 1, Width);
            if (v <= 0.0) { y0 = y1 = 0; fy = 0f; }
            else if (v >= Height - 1) { y0 = y1 = Height - 1; fy = 0f; }
            else
            {
                double fv = Math.Floor(v);
                y0 = (int)fv;
                y1 = y0 + 1;
                fy = (float)(v - fv);
            }
        }

        static int Wrap(int x, int w) => ((x % w) + w) % w;

        /// <summary>Separable box blur; wraps in longitude, clamps at the poles.</summary>
        static float[] BoxBlur(float[] src, int w, int h, int r)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float acc = 0f;
                for (int k = -r; k <= r; k++) acc += src[row + Wrap(k, w)];
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x] = acc * inv;
                    acc += src[row + Wrap(x + r + 1, w)] - src[row + Wrap(x - r, w)];
                }
            }
            for (int x = 0; x < w; x++)
            {
                float acc = 0f;
                for (int k = -r; k <= r; k++) acc += tmp[Clamp(k, h) * w + x];
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = acc * inv;
                    acc += tmp[Clamp(y + r + 1, h) * w + x] - tmp[Clamp(y - r, h) * w + x];
                }
            }
            return dst;
        }

        static int Clamp(int y, int h) => y < 0 ? 0 : (y >= h ? h - 1 : y);
    }

    /// <summary>
    /// Registry of body colour maps by key (<see cref="PlanetGenerationSettings.albedoMap"/>). The engine side loads the
    /// images and registers them BEFORE surfaces are built; a sampler created without its map falls back to the
    /// procedural colours (and without the map's relief), so every caller that builds a surface must use the same registry.
    /// Thread-safe.
    /// </summary>
    public static class SurfaceAlbedoMaps
    {
        static readonly Dictionary<string, SurfaceAlbedoMap> maps = new Dictionary<string, SurfaceAlbedoMap>(StringComparer.OrdinalIgnoreCase);
        static readonly object gate = new object();

        public static void Register(string key, SurfaceAlbedoMap map)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("empty map key");
            lock (gate)
            {
                if (map == null) maps.Remove(key);
                else maps[key] = map;
            }
        }

        public static bool TryGet(string key, out SurfaceAlbedoMap map)
        {
            map = null;
            if (string.IsNullOrEmpty(key)) return false;
            lock (gate) return maps.TryGetValue(key, out map);
        }

        public static bool IsRegistered(string key) => TryGet(key, out _);

        public static void Unregister(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (gate) maps.Remove(key);
        }
    }
}
