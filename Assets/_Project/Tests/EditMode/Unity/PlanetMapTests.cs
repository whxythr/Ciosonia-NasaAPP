using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using SpaceSim.Runtime;

namespace SpaceSim.Tests
{
    /// <summary>The artists' colour maps as Unity imports them (Resources/PlanetMaps), on the bodies that use them.</summary>
    public class PlanetMapTests
    {
        [Test]
        public void TholinMap_LoadsAndShapesTheLakes_TheRedNorthAndTheIcySouth()
        {
            Assert.IsTrue(PlanetAlbedoMapLoader.Ensure("tholin"), "Resources/PlanetMaps/tholin.png is imported and readable");
            CelestialBodyPreset b = HomeSystemCatalog.Bodies().Find(x => x.Id == HomeSystemCatalog.TholinId);
            var sampler = PlanetSurfaceSampler.Create(b.Generation, b.Physical.meanRadius);
            Assert.IsTrue(sampler.HasAlbedoMap);

            var rnd = new DeterministicRandom(23);
            int n = 4000, lakes = 0, north = 0, south = 0;
            double northRed = 0.0, southRed = 0.0, northLum = 0.0, southLum = 0.0;
            for (int i = 0; i < n; i++)
            {
                Vector3d d = rnd.UnitVector();
                SurfaceSample s = sampler.Sample(d);
                // The map's darker regions (the red tholin lowlands and the grey southern basins) become the lake beds;
                // liquid CO₂ is clear, so what is seen there is the tinted bed — count both land and lakes.
                if (s.IsWater) lakes++;
                double red = s.Albedo.r - s.Albedo.g, lum = 0.2126 * s.Albedo.r + 0.7152 * s.Albedo.g + 0.0722 * s.Albedo.b;
                if (d.z > 0.4) { north++; northRed += red; northLum += lum; }
                else if (d.z < -0.4) { south++; southRed += red; southLum += lum; }
            }
            Assert.That(lakes / (double)n, Is.InRange(0.25, 0.75), "about half the surface is CO₂ lakes");
            Assert.Greater(north, 50);
            Assert.Greater(south, 50);
            Assert.Greater(northRed / north, southRed / south + 0.03, "red tholin dust covers the north");
            Assert.Greater(southLum / south, northLum / north, "the icy south is brighter");
        }
    }
}
