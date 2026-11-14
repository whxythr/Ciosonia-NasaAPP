using System;
using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Planets;
using SpaceSim.Procedural;

namespace SpaceSim.Tests
{
    /// <summary>
    /// The start planet's water is purple; every other body keeps the ocean colours it always had.
    /// The albedo is the only per-planet input of the water shading (SpaceSimLighting.hlsl, MapBody.shader), so the checks are on it,
    /// converted the way the shaders convert it (fast sRGB → linear) and lit by the home star's colour.
    /// </summary>
    public class PlanetWaterColorTests
    {
        // Ocean colours of PlanetGenerationSettings since algorithm version 1 (Earth prototype and every body without its own palette).
        static readonly ColorRGB OriginalDeep = new ColorRGB(0.015f, 0.05f, 0.16f);
        static readonly ColorRGB OriginalShallow = new ColorRGB(0.04f, 0.17f, 0.30f);
        static readonly ColorRGB OriginalSeaIce = new ColorRGB(0.80f, 0.85f, 0.90f);

        static CelestialBodyPreset Home() => HomeSystemCatalog.Bodies().Find(b => b.Id == HomeSystemCatalog.HomeWorldId);

        /// <summary>Same polynomial as SpaceSimToLinear (SpaceSimLighting.hlsl) and MapBody.shader.</summary>
        static double ToLinear(double c) => c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878);

        static double Hue(double r, double g, double b)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            if (d <= 1e-12) return 0.0;
            double h = max == r ? 60.0 * ((g - b) / d) : max == g ? 60.0 * ((b - r) / d + 2.0) : 60.0 * ((r - g) / d + 4.0);
            return h < 0.0 ? h + 360.0 : h;
        }

        static double Saturation(ColorRGB c)
        {
            double max = Math.Max(c.r, Math.Max(c.g, c.b)), min = Math.Min(c.r, Math.Min(c.g, c.b));
            return max <= 0.0 ? 0.0 : (max - min) / max;
        }

        static void AssertExactly(ColorRGB expected, ColorRGB actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 0f, what + ".r");
            Assert.AreEqual(expected.g, actual.g, 0f, what + ".g");
            Assert.AreEqual(expected.b, actual.b, 0f, what + ".b");
        }

        [Test]
        public void DefaultSettings_KeepTheOriginalOceanColours()
        {
            var s = new PlanetGenerationSettings();
            AssertExactly(OriginalDeep, s.deepOcean, "deepOcean");
            AssertExactly(OriginalShallow, s.shallowOcean, "shallowOcean");
            AssertExactly(OriginalSeaIce, s.seaIce, "seaIce");
        }

        [Test]
        public void OtherCatalogBodies_KeepTheOriginalOceanColours()
        {
            foreach (CelestialBodyPreset b in HomeSystemCatalog.Bodies())
            {
                if (b.Id == HomeSystemCatalog.HomeWorldId) continue;
                // Tholin's lakes are liquid CO₂, not water: a new body with its own (colourless-liquid) lake colours.
                if (b.Id == HomeSystemCatalog.TholinId) continue;
                AssertExactly(OriginalDeep, b.Generation.deepOcean, b.Id + " deepOcean");
                AssertExactly(OriginalShallow, b.Generation.shallowOcean, b.Id + " shallowOcean");
                AssertExactly(OriginalSeaIce, b.Generation.seaIce, b.Id + " seaIce");
            }
        }

        [Test]
        public void DefaultOcean_SamplesKeepTheOriginalColours()
        {
            // An Earth-sized default world: deep, warm water must be exactly the original deep colour (no ice blend).
            var sampler = PlanetSurfaceSampler.Create(new PlanetGenerationSettings(), 6371000.0);
            var rng = new DeterministicRandom(7);
            int deep = 0;
            for (int i = 0; i < 4000 && deep < 50; i++)
            {
                Vector3d d = rng.UnitVector();
                if (Math.Abs(d.z) > 0.5) continue; // |lat| < 30°: far from sea ice
                SurfaceSample s = sampler.Sample(d);
                if (!s.IsWater || s.TerrainHeight > -2600.0) continue;
                deep++;
                Assert.AreEqual(OriginalDeep.r, s.Albedo.r, 1e-6, "r");
                Assert.AreEqual(OriginalDeep.g, s.Albedo.g, 1e-6, "g");
                Assert.AreEqual(OriginalDeep.b, s.Albedo.b, 1e-6, "b");
            }
            Assert.Greater(deep, 10, "found deep water");
        }

        [Test]
        public void HomeOcean_SamplesArePurple()
        {
            CelestialBodyPreset h = Home();
            var sampler = PlanetSurfaceSampler.Create(h.Generation, h.Physical.meanRadius);
            var rng = new DeterministicRandom(31);
            int water = 0, saturated = 0;
            for (int i = 0; i < 5000; i++)
            {
                SurfaceSample s = sampler.Sample(rng.UnitVector());
                if (!s.IsWater) continue;
                water++;
                ColorRGB c = s.Albedo;
                Assert.Greater(c.b, c.g, "blue > green");
                Assert.Greater(c.r, c.g, "red > green");
                double hue = Hue(c.r, c.g, c.b);
                Assert.That(hue, Is.InRange(255.0, 300.0), "violet/purple hue, albedo " + c.r + ", " + c.g + ", " + c.b);
                if (s.Biome != Biome.SeaIce && Saturation(c) > 0.6) saturated++;
            }
            Assert.Greater(water, 1000, "has oceans");
            Assert.Greater(saturated, water * 0.9, "open water is strongly coloured, not grey");
        }

        [Test]
        public void HomeOcean_IsBrightEnoughToShowThroughTheSkyReflection()
        {
            // At normal incidence water mirrors 2 % of the (blue) sky. The old deep violet (0.07, 0.02, 0.16) had ≈ 0.3 % linear
            // luminance and rendered navy; ocean-like ≈ 2.5…8 % keeps the body colour visible without looking like paint.
            ColorRGB[] colours = { Home().Generation.deepOcean, Home().Generation.shallowOcean };
            foreach (ColorRGB c in colours)
            {
                double lum = 0.2126 * ToLinear(c.r) + 0.7152 * ToLinear(c.g) + 0.0722 * ToLinear(c.b);
                Assert.That(lum, Is.InRange(0.025, 0.12), "linear luminance of " + c.r + ", " + c.g + ", " + c.b);
                // SpaceSimShadeSurface treats only albedo with sRGB luminance < 0.45 as open water.
                Assert.Less(0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b, 0.45, "still shaded as water");
            }
        }

        [Test]
        public void HomeOcean_ReadsPurpleUnderItsKStarLight()
        {
            ColorRGB star = HomeSystemCatalog.Star().color;
            CelestialBodyPreset h = Home();
            foreach (ColorRGB c in new[] { h.Generation.deepOcean, h.Generation.shallowOcean })
            {
                double r = ToLinear(c.r) * ToLinear(star.r), g = ToLinear(c.g) * ToLinear(star.g), b = ToLinear(c.b) * ToLinear(star.b);
                double hue = Hue(r, g, b);
                // 270° violet … 300° purple-magenta: not blue (< 260°) and not pink (> 310°) in the warm starlight.
                Assert.That(hue, Is.InRange(270.0, 300.0), "lit hue of " + c.r + ", " + c.g + ", " + c.b);
            }
            // Deep water darker than the coasts, sea ice lighter than both.
            Assert.Less(h.Generation.deepOcean.r + h.Generation.deepOcean.g + h.Generation.deepOcean.b,
                h.Generation.shallowOcean.r + h.Generation.shallowOcean.g + h.Generation.shallowOcean.b);
            Assert.Less(h.Generation.shallowOcean.r + h.Generation.shallowOcean.g + h.Generation.shallowOcean.b,
                h.Generation.seaIce.r + h.Generation.seaIce.g + h.Generation.seaIce.b);
        }
    }
}
