using System;
using System.Threading.Tasks;
using SpaceSim.Procedural;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The tiling 3D noise every procedural shader reads (cloud shapes, ground detail, flame turbulence, smoke): generated once from a fixed
    /// seed on a worker thread, no asset. RGBA: r = Perlin-Worley (billowy forms), g/b/a = inverted Worley fBm at growing frequencies.
    /// Bound as the global texture <c>_SpaceSimNoise3D</c>; until it is ready a small grey placeholder stands in.
    /// </summary>
    public static class SpaceSimNoiseTextures
    {
        public const int Size = 64;
        const ulong Seed = 0x5EEDC10D5UL;

        static readonly int NoiseId = Shader.PropertyToID("_SpaceSimNoise3D");
        static Texture3D texture, placeholder;
        static Task<byte[]> job;

        /// <summary>True once the real noise is bound.</summary>
        public static bool Ready => texture != null;

        /// <summary>Call every frame: starts the job, uploads the result. Cheap once done.</summary>
        public static void Tick()
        {
            if (texture != null) return;
            if (placeholder == null)
            {
                placeholder = Create(4);
                var grey = new byte[4 * 4 * 4 * 4];
                for (int i = 0; i < grey.Length; i++) grey[i] = 128;
                placeholder.SetPixelData(grey, 0);
                placeholder.Apply(false, true);
                Shader.SetGlobalTexture(NoiseId, placeholder);
            }
            if (job == null) job = Task.Run(Generate);
            if (!job.IsCompleted) return;
            if (job.IsFaulted)
            {
                Debug.LogError("[SpaceSim] Noise generation failed: " + job.Exception);
                job = Task.Run(Generate);
                return;
            }
            texture = Create(Size);
            texture.SetPixelData(job.Result, 0);
            texture.Apply(true, true);
            Shader.SetGlobalTexture(NoiseId, texture);
            job = null;
        }

        static Texture3D Create(int size) =>
            new Texture3D(size, size, size, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.MipChain)
            {
                name = "SpaceSim Noise 3D",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };

        sealed class Worley
        {
            readonly int frequency;
            readonly float[] points;

            public Worley(int cells, ulong seed)
            {
                frequency = cells;
                var rng = new DeterministicRandom(seed);
                points = new float[cells * cells * cells * 3];
                for (int i = 0; i < points.Length; i++) points[i] = (float)rng.NextDouble();
            }

            /// <summary>1 at a feature point falling to 0 far from every point; (x, y, z) in tile units.</summary>
            public double Sample(double x, double y, double z)
            {
                double fx = x * frequency, fy = y * frequency, fz = z * frequency;
                int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy), iz = (int)Math.Floor(fz);
                double best = 3.0;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int cx = Wrap(ix + dx, frequency), cy = Wrap(iy + dy, frequency), cz = Wrap(iz + dz, frequency);
                            int p = (cx + frequency * (cy + frequency * cz)) * 3;
                            double px = ix + dx + points[p] - fx;
                            double py = iy + dy + points[p + 1] - fy;
                            double pz = iz + dz + points[p + 2] - fz;
                            double d2 = px * px + py * py + pz * pz;
                            if (d2 < best) best = d2;
                        }
                return 1.0 - Math.Min(1.0, Math.Sqrt(best));
            }
        }

        static int Wrap(int v, int period)
        {
            int m = v % period;
            return m < 0 ? m + period : m;
        }

        static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

        static double Gradient(int hash, double x, double y, double z)
        {
            int h = hash & 15;
            double u = h < 8 ? x : y;
            double v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        static double Perlin(double x, double y, double z, int period, int[] perm)
        {
            double fx0 = Math.Floor(x), fy0 = Math.Floor(y), fz0 = Math.Floor(z);
            int x0 = Wrap((int)fx0, period), x1 = Wrap((int)fx0 + 1, period);
            int y0 = Wrap((int)fy0, period), y1 = Wrap((int)fy0 + 1, period);
            int z0 = Wrap((int)fz0, period), z1 = Wrap((int)fz0 + 1, period);
            double fx = x - fx0, fy = y - fy0, fz = z - fz0;
            double u = Fade(fx), v = Fade(fy), w = Fade(fz);
            double L(double a, double b, double t) => a + t * (b - a);
            int H(int i, int j, int k) => perm[(perm[(perm[i] + j) & 255] + k) & 255];
            return L(L(L(Gradient(H(x0, y0, z0), fx, fy, fz), Gradient(H(x1, y0, z0), fx - 1, fy, fz), u),
                       L(Gradient(H(x0, y1, z0), fx, fy - 1, fz), Gradient(H(x1, y1, z0), fx - 1, fy - 1, fz), u), v),
                     L(L(Gradient(H(x0, y0, z1), fx, fy, fz - 1), Gradient(H(x1, y0, z1), fx - 1, fy, fz - 1), u),
                       L(Gradient(H(x0, y1, z1), fx, fy - 1, fz - 1), Gradient(H(x1, y1, z1), fx - 1, fy - 1, fz - 1), u), v), w);
        }

        static byte[] Generate()
        {
            var rng = new DeterministicRandom(Seed);
            var perm = new int[256];
            for (int i = 0; i < 256; i++) perm[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                int tmp = perm[i];
                perm[i] = perm[j];
                perm[j] = tmp;
            }

            Worley w4 = new Worley(4, DeterministicRandom.Mix(Seed, 4)), w8 = new Worley(8, DeterministicRandom.Mix(Seed, 8));
            Worley w16 = new Worley(16, DeterministicRandom.Mix(Seed, 16)), w24 = new Worley(24, DeterministicRandom.Mix(Seed, 24));
            Worley w32 = new Worley(32, DeterministicRandom.Mix(Seed, 32));

            var channels = new float[Size * Size * Size * 4];
            Parallel.For(0, Size, z =>
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        double u = (x + 0.5) / Size, v = (y + 0.5) / Size, w = (z + 0.5) / Size;
                        double a4 = w4.Sample(u, v, w), a8 = w8.Sample(u, v, w), a16 = w16.Sample(u, v, w);
                        double a24 = w24.Sample(u, v, w), a32 = w32.Sample(u, v, w);
                        double shape = 0.625 * a4 + 0.25 * a8 + 0.125 * a16;
                        double perlin = 0.5 + 0.5 * (0.6 * Perlin(u * 4, v * 4, w * 4, 4, perm) + 0.3 * Perlin(u * 8, v * 8, w * 8, 8, perm)
                                                     + 0.1 * Perlin(u * 16, v * 16, w * 16, 16, perm));
                        double perlinWorley = (perlin - (shape - 1.0)) / (2.0 - shape); // Perlin dilated by Worley: billows with holes
                        int t = (x + Size * (y + Size * z)) * 4;
                        channels[t] = (float)perlinWorley;
                        channels[t + 1] = (float)shape;
                        channels[t + 2] = (float)(0.625 * a8 + 0.25 * a16 + 0.125 * a24);
                        channels[t + 3] = (float)(0.625 * a16 + 0.25 * a24 + 0.125 * a32);
                    }
                }
            });

            // Full 8-bit range in every channel.
            var bytes = new byte[channels.Length];
            for (int c = 0; c < 4; c++)
            {
                float min = float.MaxValue, max = float.MinValue;
                for (int t = c; t < channels.Length; t += 4)
                {
                    if (channels[t] < min) min = channels[t];
                    if (channels[t] > max) max = channels[t];
                }
                float scale = 255.0f / Math.Max(max - min, 1e-5f);
                for (int t = c; t < channels.Length; t += 4) bytes[t] = (byte)Math.Round((channels[t] - min) * scale);
            }
            return bytes;
        }
    }
}
