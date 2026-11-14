using System;
using System.Threading.Tasks;
using SpaceSim.Mathematics;
using SpaceSim.Procedural;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The large-scale weather of a planet: where clouds are, what kind, in planet-fixed axes (Unity: +Y = north pole). A deterministic function
    /// of the planet's seed, computed once on worker threads and stored in an equirectangular texture so that a shader needs one read per
    /// sample. Warped fBm + swirling cyclones in the storm tracks + a latitudinal climate. The coverage threshold is taken from the actual
    /// distribution of the field, so <c>cloudCoverage</c> really is the fraction of the sphere under cloud.
    /// </summary>
    public sealed class CloudWeatherMap
    {
        public int Width, Height;
        /// <summary>RGBA8, row 0 = south pole. r coverage, g cloud type, b/a large-scale noise warp.</summary>
        public byte[] Pixels;
        /// <summary>Coverage on a coarse grid for the CPU (the sunlight on objects under a cloud).</summary>
        public byte[] Coarse;
        public const int CoarseWidth = 256, CoarseHeight = 128;

        struct Cyclone
        {
            public Vector3d Center;
            public double CosRadius, Radius, Strength;
        }

        public static CloudWeatherMap Generate(int seed, double coverage, int width)
        {
            ulong s = (uint)seed;
            int height = width / 2;
            var baseNoise = new GradientNoise3D(DeterministicRandom.Mix(s, 0xC10D01));
            var warpNoise = new GradientNoise3D(DeterministicRandom.Mix(s, 0xC10D02));
            var typeNoise = new GradientNoise3D(DeterministicRandom.Mix(s, 0xC10D03));
            var driftNoise = new GradientNoise3D(DeterministicRandom.Mix(s, 0xC10D04));
            Cyclone[] storms = BuildCyclones(s);

            // Threshold from the area-weighted distribution of the field (Fibonacci sphere: equal area per sample).
            const int probes = 4096;
            var values = new double[probes];
            Parallel.For(0, probes, i =>
            {
                double y = 1.0 - (2.0 * i + 1.0) / probes;
                double ring = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                double a = i * 2.39996322972865;
                values[i] = Field(new Vector3d(ring * Math.Cos(a), y, ring * Math.Sin(a)), baseNoise, warpNoise, storms);
            });
            Array.Sort(values);
            int cut = Math.Min(probes - 1, Math.Max(0, (int)Math.Round((1.0 - MathD.Clamp01(coverage)) * (probes - 1))));
            double threshold = values[cut];

            var map = new CloudWeatherMap { Width = width, Height = height, Pixels = new byte[width * height * 4], Coarse = new byte[CoarseWidth * CoarseHeight] };
            Parallel.For(0, height, j =>
            {
                double lat = Math.PI * ((j + 0.5) / height - 0.5);
                double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
                for (int i = 0; i < width; i++)
                {
                    double lon = 2.0 * Math.PI * ((i + 0.5) / width - 0.5);
                    var p = new Vector3d(cosLat * Math.Cos(lon), sinLat, cosLat * Math.Sin(lon));
                    double v = Field(p, baseNoise, warpNoise, storms);
                    double cells = 0.5 + 0.9 * typeNoise.Fbm(p * 38.0 + new Vector3d(5.3, 1.7, 9.2), 3, 2.1, 0.55); // ~150 km mottling inside the decks
                    // In rank units (fraction of the sphere below v), so the deck covers what cloudCoverage says whatever the noise statistics.
                    double rank = Rank(values, v), clear = 1.0 - MathD.Clamp01(coverage);
                    double cov = MathD.SmoothStep(clear - 0.07, clear + 0.10, rank) * MathD.Clamp(0.3 + 0.8 * cells, 0.15, 1.0);
                    double type = MathD.Clamp01(0.5 + 1.4 * typeNoise.Fbm(p * 3.1, 4, 2.0, 0.5) + 0.35 * (v - threshold));
                    double wx = 0.5 + 0.7 * driftNoise.Fbm(p * 5.3, 3, 2.0, 0.5);
                    double wy = 0.5 + 0.7 * driftNoise.Fbm(new Vector3d(p.z, p.x, p.y) * 5.3 + new Vector3d(7.1, 3.3, 11.9), 3, 2.0, 0.5);
                    int t = (i + width * j) * 4;
                    map.Pixels[t] = ToByte(cov);
                    map.Pixels[t + 1] = ToByte(type);
                    map.Pixels[t + 2] = ToByte(wx);
                    map.Pixels[t + 3] = ToByte(wy);
                }
            });
            BlurCoverage(map.Pixels, width, height);
            BlurCoverage(map.Pixels, width, height);
            for (int j = 0; j < CoarseHeight; j++)
                for (int i = 0; i < CoarseWidth; i++)
                {
                    int x = (i * 2 + 1) * width / (CoarseWidth * 2), y = (j * 2 + 1) * height / (CoarseHeight * 2);
                    map.Coarse[i + j * CoarseWidth] = map.Pixels[(x + y * width) * 4];
                }
            return map;
        }

        /// <summary>Coverage 0..1 towards a unit direction in the cloud frame (nearest coarse sample).</summary>
        public float CoverageAt(double x, double y, double z)
        {
            double lon = Math.Atan2(z, x) / (2.0 * Math.PI) + 0.5;
            double lat = Math.Asin(MathD.Clamp(y, -1.0, 1.0)) / Math.PI + 0.5;
            int i = Math.Min(CoarseWidth - 1, (int)(lon * CoarseWidth));
            int j = Math.Min(CoarseHeight - 1, (int)(lat * CoarseHeight));
            return Coarse[i + j * CoarseWidth] / 255f;
        }

        /// <summary>3×3 box blur of the coverage channel (x wraps): a soft ramp at the deck edges, so the noise, not the texels, shapes them.</summary>
        static void BlurCoverage(byte[] pixels, int width, int height)
        {
            var source = new byte[width * height];
            for (int k = 0; k < source.Length; k++) source[k] = pixels[k * 4];
            Parallel.For(0, height, j =>
            {
                int j0 = Math.Max(0, j - 1), j1 = Math.Min(height - 1, j + 1);
                for (int i = 0; i < width; i++)
                {
                    int sum = 0, n = 0;
                    for (int y = j0; y <= j1; y++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            sum += source[(i + dx + width) % width + y * width];
                            n++;
                        }
                    pixels[(i + j * width) * 4] = (byte)((sum + n / 2) / n);
                }
            });
        }

        /// <summary>Fraction of the sorted probes below v (binary search, linear between neighbours).</summary>
        static double Rank(double[] sorted, double v)
        {
            int lo = 0, hi = sorted.Length - 1;
            if (v <= sorted[lo]) return 0.0;
            if (v >= sorted[hi]) return 1.0;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (sorted[mid] <= v) lo = mid; else hi = mid;
            }
            double f = (v - sorted[lo]) / Math.Max(sorted[hi] - sorted[lo], 1e-12);
            return (lo + f) / (sorted.Length - 1);
        }

        static byte ToByte(double v) => (byte)Math.Round(MathD.Clamp01(v) * 255.0);

        static Cyclone[] BuildCyclones(ulong seed)
        {
            var rng = new DeterministicRandom(DeterministicRandom.Mix(seed, 0xC10D05));
            var list = new Cyclone[14];
            for (int i = 0; i < list.Length; i++)
            {
                double side = i % 2 == 0 ? 1.0 : -1.0;
                double lat = side * rng.Range(30.0, 65.0) * MathD.DegToRad;
                double lon = rng.Range(-Math.PI, Math.PI);
                double radius = rng.Range(0.09, 0.26);
                list[i] = new Cyclone
                {
                    Center = new Vector3d(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon)),
                    Radius = radius,
                    CosRadius = Math.Cos(radius),
                    Strength = side * rng.Range(2.0, 3.8)
                };
            }
            return list;
        }

        /// <summary>The scalar cloudiness field; only its ranking matters.</summary>
        static double Field(Vector3d p, GradientNoise3D baseNoise, GradientNoise3D warpNoise, Cyclone[] storms)
        {
            Vector3d q = p;
            for (int i = 0; i < storms.Length; i++)
            {
                double cosA = Vector3d.Dot(p, storms[i].Center);
                if (cosA <= storms[i].CosRadius) continue;
                double a = Math.Acos(Math.Min(1.0, cosA));
                double k = 1.0 - a / storms[i].Radius;
                double angle = storms[i].Strength * k * k;
                Vector3d c = storms[i].Center;
                q = q * Math.Cos(angle) + Vector3d.Cross(c, q) * Math.Sin(angle) + c * (Vector3d.Dot(c, q) * (1.0 - Math.Cos(angle)));
            }
            var warp = new Vector3d(warpNoise.Fbm(q * 2.4, 3, 2.1, 0.5),
                warpNoise.Fbm(q * 2.4 + new Vector3d(31.7, 11.3, 5.9), 3, 2.1, 0.5),
                warpNoise.Fbm(q * 2.4 + new Vector3d(9.1, 47.3, 23.1), 3, 2.1, 0.5));
            double n = baseNoise.Fbm((q + warp * 0.28) * 4.2, 8, 2.08, 0.56);
            // Wet equator, dry subtropics, stormy mid-latitudes, clear poles — only a bias on top of the noise.
            double latitude = Math.Asin(MathD.Clamp(p.y, -1.0, 1.0));
            double climate = 0.04 * Math.Cos(6.0 * latitude) * (1.0 - 0.5 * Math.Abs(p.y));
            return 0.5 + 0.5 * n + climate;
        }
    }
}
