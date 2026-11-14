using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Sunlight transmittance of one atmosphere for any height and sun angle, baked into a small texture with the physics code itself
    /// (<see cref="AtmosphereOptics.Transmittance"/>, the same function that dims the Unity light). Every shader reads this one table, so
    /// terrain, clouds, sky and smoke redden together at sunset. Visual only. The (x, y) ↔ (height, angle) mapping is Bruneton's non-linear
    /// one and must match SSTransmittanceUV in SpaceSimLighting.hlsl.
    /// </summary>
    public static class AtmosphereTransmittanceLut
    {
        public const int Width = 128, Height = 32;
        const int Steps = 32;

        /// <param name="radius">Reference radius [m].</param>
        /// <param name="shellHeight">Top of the visual atmosphere above it [m].</param>
        public static Texture2D Build(IAtmosphereProfile profile, AtmosphereOpticalProperties optics, double radius, double shellHeight, string name)
        {
            double top = radius + shellHeight;
            double H = Math.Sqrt(shellHeight * (2.0 * radius + shellHeight));
            var pixels = new Color[Width * Height];
            for (int j = 0; j < Height; j++)
            {
                double rho = H * j / (Height - 1.0);
                double r = Math.Sqrt(rho * rho + radius * radius);
                double dMin = top - r, dMax = rho + H;
                var origin = new Vector3d(0.0, 0.0, r);
                for (int i = 0; i < Width; i++)
                {
                    // The last column would be the exact tangent ray, which the planet's sphere test may count as blocked.
                    double d = dMin + (dMax - dMin) * (i / (Width - 1.0)) * 0.9999;
                    double mu = d <= 1e-3 ? 1.0 : MathD.Clamp((H * H - rho * rho - d * d) / (2.0 * r * d), -1.0, 1.0);
                    var direction = new Vector3d(Math.Sqrt(Math.Max(0.0, 1.0 - mu * mu)), 0.0, mu);
                    Vector3d t = AtmosphereOptics.Transmittance(profile, optics, radius, top, origin, direction, Steps);
                    pixels[i + j * Width] = new Color((float)t.x, (float)t.y, (float)t.z, 1f);
                }
            }
            var lut = new Texture2D(Width, Height, TextureFormat.RGBAHalf, false, true)
            {
                name = "Transmittance · " + name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            lut.SetPixels(pixels);
            lut.Apply(false, true);
            return lut;
        }
    }
}
