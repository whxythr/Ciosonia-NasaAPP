using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;

namespace SpaceSim.Tests
{
    /// <summary>The start planet built from the artists' map: violet = sea, the map decides the coastline, names of the system.</summary>
    public class CiosoniaMapTests
    {
        const string TestKey = "unit-test-ciosonia";

        [TearDown]
        public void TearDown() => SurfaceAlbedoMaps.Unregister(TestKey);

        [Test]
        public void WaterScore_VioletIsSea_GreenOchreAndIceAreLand()
        {
            Assert.AreEqual(1f, SurfaceAlbedoMap.WaterScore(65 / 255f, 55 / 255f, 88 / 255f), 1e-6f, "violet sea of the map");
            Assert.AreEqual(0f, SurfaceAlbedoMap.WaterScore(73 / 255f, 93 / 255f, 54 / 255f), 1e-6f, "green plain");
            Assert.AreEqual(0f, SurfaceAlbedoMap.WaterScore(200 / 255f, 190 / 255f, 120 / 255f), 1e-6f, "ochre desert");
            Assert.AreEqual(0f, SurfaceAlbedoMap.WaterScore(172 / 255f, 174 / 255f, 176 / 255f), 1e-6f, "polar ice");
        }

        /// <summary>16×8 map: west half violet sea, east half green land.</summary>
        static SurfaceAlbedoMap HalfSeaMap()
        {
            const int w = 16, h = 8;
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool sea = x < w / 2;
                px[i] = (byte)(sea ? 65 : 73);
                px[i + 1] = (byte)(sea ? 55 : 93);
                px[i + 2] = (byte)(sea ? 88 : 54);
                px[i + 3] = 255;
            }
            return SurfaceAlbedoMap.FromSrgb32(w, h, px, 1);
        }

        [Test]
        public void MapDecidesLandAndSea_AndColoursTheLand()
        {
            CelestialBodyPreset home = HomeSystemCatalog.Bodies().Find(b => b.Id == HomeSystemCatalog.HomeWorldId);
            Assert.AreEqual("ciosonia", home.Generation.albedoMap);
            Assert.Greater(home.Generation.albedoMapCoastline, 0.0);

            var settings = home.Generation.Clone();
            settings.albedoMap = TestKey;
            settings.flattenZones = new FlattenZone[0];
            SurfaceAlbedoMaps.Register(TestKey, HalfSeaMap());
            var sampler = PlanetSurfaceSampler.Create(settings, home.Physical.meanRadius);
            Assert.IsTrue(sampler.HasAlbedoMap);

            int seaWet = 0, landDry = 0;
            for (int k = 0; k < 20; k++)
            {
                double lat = (-40.0 + 4.0 * k) * MathD.DegToRad;
                if (sampler.Sample(PlanetRotation.DirectionFromLatLon(lat, -90.0 * MathD.DegToRad)).IsWater) seaWet++;
                SurfaceSample land = sampler.Sample(PlanetRotation.DirectionFromLatLon(lat, 90.0 * MathD.DegToRad));
                if (!land.IsWater && land.Albedo.g > land.Albedo.r) landDry++;
            }
            Assert.AreEqual(20, seaWet, "the map's violet half is sea everywhere far from its coast");
            Assert.GreaterOrEqual(landDry, 18, "the green half is green land (a few high peaks may be rock or snow)");
        }

        [Test]
        public void SystemNames_FollowTheDesigner()
        {
            Assert.AreEqual("Люмини A (Lumini A)", HomeSystemCatalog.Star().name);
            var bodies = HomeSystemCatalog.Bodies();
            Assert.AreEqual("Циосония (Ciosonia)", bodies.Find(b => b.Id == HomeSystemCatalog.HomeWorldId).DisplayName);
            Assert.AreEqual("Эгида (Aegis)", bodies.Find(b => b.Id == HomeSystemCatalog.MoraMoonId).DisplayName);
            foreach (string id in new[] { HomeSystemCatalog.LinId, HomeSystemCatalog.TiafoeId })
            {
                CelestialBodyPreset m = bodies.Find(b => b.Id == id);
                Assert.IsNotNull(m, id);
                Assert.AreEqual(HomeSystemCatalog.TholinId, m.ParentId);
                // Deep inside Tholin's Hill sphere, and even the pericentre outside the rigid Roche limit (≈ 1.8 R).
                CelestialBodyPreset tholin = bodies.Find(b => b.Id == HomeSystemCatalog.TholinId);
                Assert.Greater(m.Orbit.semiMajorAxis * (1.0 - m.Orbit.eccentricity), 2.5 * tholin.Physical.meanRadius);
                Assert.Less(m.Orbit.semiMajorAxis, 0.3e9);
            }
        }
    }
}
