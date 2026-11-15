using System;
using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;

namespace SpaceSim.Tests
{
    /// <summary>Kolimpri and Tromos — the start planet's moons — and the artists' colour maps of bodies.</summary>
    public class HomeMoonsTests
    {
        const double Day = 86400.0;
        static CelestialBodyPreset Body(string id) => HomeSystemCatalog.Bodies().Find(b => b.Id == id);
        static double HomeMu => Body(HomeSystemCatalog.HomeWorldId).Physical.GravitationalParameter;

        [TestCase(HomeSystemCatalog.KolimpriId, 1.43e21, 427406.0, 4440.0, 0.531)]
        [TestCase(HomeSystemCatalog.TromosId, 3.31e20, 266478.0, 4180.0, 0.311)]
        public void Moons_MatchDesignData(string id, double mass, double radius, double density, double g)
        {
            CelestialBodyPreset b = Body(id);
            Assert.AreEqual(HomeSystemCatalog.HomeWorldId, b.ParentId, "both orbit the start planet");
            Assert.AreEqual(mass, b.Physical.mass, mass * 1e-9);
            Assert.AreEqual(radius, b.Physical.meanRadius, 1e-6);
            // The designer's ρ and g follow from M and R within 2 % (Kolimpri: 4373 vs 4440, 0.522 vs 0.531).
            Assert.AreEqual(density, b.Physical.MeanDensity, density * 0.02, "ρ = M / (4/3·π·R³)");
            Assert.AreEqual(g, b.Physical.SurfaceGravity, g * 0.02, "g = GM/R²");
            Assert.IsEmpty(b.Physical.Validate());
        }

        [TestCase(HomeSystemCatalog.KolimpriId, 56888489.0, 1.70)]
        [TestCase(HomeSystemCatalog.TromosId, 96890115.0, 3.80)]
        public void Moons_PeriodFromKepler_MatchesTheGivenPeriod(string id, double a, double periodDays)
        {
            var orbit = new KeplerOrbit(Body(id).Orbit, HomeMu);
            Assert.AreEqual(a, orbit.SemiMajorAxis, 1e-6, "the designer's semi-major axis");
            Assert.AreEqual(periodDays * Day, orbit.Period, periodDays * Day * 0.005, "the start planet's mass reproduces the quoted period");
        }

        [Test]
        public void Kolimpri_IsTidallyLocked_PrimeMeridianFacesThePlanet()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.KolimpriId);
            var orbit = new KeplerOrbit(b.Orbit, HomeMu);
            Assert.AreEqual(orbit.Period, b.Physical.siderealRotationPeriod, 1e-6);
            var rot = new PlanetRotation(b.Physical.siderealRotationPeriod, b.Physical.axialTiltDegrees * MathD.DegToRad,
                b.Physical.rotationPhaseAtEpochDegrees * MathD.DegToRad);
            // Eccentric (e = 0.039) and inclined (6.51°) orbit: the planet librates by up to 2e + 1.25e² ≈ 4.5°.
            double e = b.Orbit.eccentricity;
            foreach (double t in new[] { 0.0, 4.0e4, 1.3e5, 9.0e5 })
            {
                Vector3d toPlanet = (-orbit.Position(t)).Normalized;
                Assert.Greater(Vector3d.Dot(toPlanet, rot.FixedToInertial(Vector3d.UnitX, t)), Math.Cos(2.0 * e + 1.25 * e * e + 1e-3));
            }
            Assert.IsFalse(b.Physical.atmosphere.hasAtmosphere, "no atmosphere at all");
            Assert.AreEqual(-133.5, b.Physical.atmosphere.surfaceTemperature - 273.15, 0.01, "mean of −111 … −156 °C");
        }

        [Test]
        public void Tromos_RotatesFreely_AndHasThinArgonAir()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.TromosId);
            Assert.IsFalse(b.Orbit.tidallyLocked);
            Assert.AreEqual(2.55 * Day, b.Physical.siderealRotationPeriod, 1e-6);

            AtmosphereProperties a = b.Physical.atmosphere;
            Assert.IsTrue(a.hasAtmosphere);
            Assert.AreEqual(-42.6, a.surfaceTemperature - 273.15, 1e-9);
            StringAssert.Contains("Ar", a.composition);
            // H = RT/(Mg): tiny gravity makes the thin air very extended.
            Assert.AreEqual(8.314462618 * 230.55 / (0.0373 * b.Physical.SurfaceGravity), a.scaleHeight, 200.0);
            Assert.Greater(a.scaleHeight, 0.5 * b.Physical.meanRadius);
            var profile = AtmosphericModel.Create(a);
            Assert.AreEqual(100.0, profile.Pressure(0.0), 1e-6);
            Assert.Less(profile.ReferenceDensity, 0.01, "thin: ≈ 2·10⁻³ kg/m³");

            // Faint: optical depth of the column is a few % of the start planet's.
            CelestialBodyPreset home = Body(HomeSystemCatalog.HomeWorldId);
            double tau = b.Optics.rayleighScattering.y * a.scaleHeight;
            double tauHome = home.Optics.rayleighScattering.y * home.Physical.atmosphere.scaleHeight;
            Assert.That(tau / tauHome, Is.InRange(0.005, 0.1));

            // The air is enough to keep radiation off the surface (consequence of the hazard rules), not above it.
            Assert.AreEqual(0, HazardModel.Evaluate(b.Hazards, 1.0, 0, 0).Radiation);
            Assert.AreEqual(1, HazardModel.Evaluate(b.Hazards, 0.0, 0, 0).Radiation);
        }

        [Test]
        public void Moons_AreInsideThePlanetsSphereOfInfluence()
        {
            var sys = PatchedConicSystem.FromHomeSystem();
            int home = sys.IndexOf(HomeSystemCatalog.HomeWorldId);
            foreach (string id in new[] { HomeSystemCatalog.KolimpriId, HomeSystemCatalog.TromosId })
            {
                int i = sys.IndexOf(id);
                Assert.GreaterOrEqual(i, 0, id);
                Assert.AreEqual(home, sys.Parent(i));
                Assert.Less(sys[i].Orbit.SemiMajorAxis + sys[i].SoiRadius, sys[home].SoiRadius, id + " lies well inside the planet's SOI");
                Assert.Greater(sys[i].SoiRadius, 3.0 * sys[i].Radius - 1.0);
            }
        }

        [Test]
        public void Tromos_CanyonIsCutIntoTheSmoothSurface()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.TromosId);
            FeatureCanyon c = b.Generation.featureCanyons[0];
            var with = PlanetSurfaceSampler.Create(b.Generation, b.Physical.meanRadius);
            var flat = b.Generation.Clone();
            flat.featureCanyons = new FeatureCanyon[0];
            var without = PlanetSurfaceSampler.Create(flat, b.Physical.meanRadius);

            Vector3d s = PlanetRotation.DirectionFromLatLon(c.startLatitudeDegrees * MathD.DegToRad, c.startLongitudeDegrees * MathD.DegToRad);
            Vector3d e = PlanetRotation.DirectionFromLatLon(c.endLatitudeDegrees * MathD.DegToRad, c.endLongitudeDegrees * MathD.DegToRad);
            Vector3d mid = (s + e).Normalized;
            Assert.AreEqual(-c.depth, with.TerrainHeight(mid) - without.TerrainHeight(mid), 1.0, "full depth on the axis");
            Assert.Less(with.Sample(mid).Albedo.g, without.Sample(mid).Albedo.g, "darker floor");

            // Outside the walls and beyond the ends nothing changes.
            Vector3d n = Vector3d.Cross(s, e).Normalized;
            Vector3d beside = (mid + n * (0.6 * c.width / b.Physical.meanRadius)).Normalized;
            Assert.AreEqual(without.TerrainHeight(beside), with.TerrainHeight(beside), 1e-9);
            Vector3d beyond = (e * 2.0 - mid).Normalized;
            Assert.AreEqual(without.TerrainHeight(beyond), with.TerrainHeight(beyond), 1e-9);
            Assert.LessOrEqual(with.MinTerrainHeight, with.TerrainHeight(mid));

            // "No big craters": the generator's craters stay small on Tromos.
            Assert.LessOrEqual(b.Generation.craterMaxRadius, 5000.0);
        }

        // ---- Artist colour maps ----

        const string TestKey = "unit-test-map";

        [TearDown]
        public void TearDown() => SurfaceAlbedoMaps.Unregister(TestKey);

        /// <summary>8×4 map, rows south→north: west half red, east half blue; the southern row is black.</summary>
        static SurfaceAlbedoMap TwoColourMap()
        {
            const int w = 8, h = 4;
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool west = x < w / 2;
                byte v = (byte)(y == 0 ? 0 : 255);
                px[i] = west ? v : (byte)0;
                px[i + 2] = west ? (byte)0 : v;
                px[i + 3] = 255;
            }
            return SurfaceAlbedoMap.FromSrgb32(w, h, px, 1);
        }

        [Test]
        public void AlbedoMap_EquirectangularLayout_LongitudeZeroIsTheCentre()
        {
            SurfaceAlbedoMap map = TwoColourMap();
            ColorRGB west = map.Sample(PlanetRotation.DirectionFromLatLon(20.0 * MathD.DegToRad, -90.0 * MathD.DegToRad));
            ColorRGB east = map.Sample(PlanetRotation.DirectionFromLatLon(20.0 * MathD.DegToRad, 90.0 * MathD.DegToRad));
            Assert.Greater(west.r, 0.9f);
            Assert.Less(west.b, 0.05f);
            Assert.Greater(east.b, 0.9f);
            Assert.Less(east.r, 0.05f);
            ColorRGB south = map.Sample(new Vector3d(0, 0, -1));
            Assert.Less(south.r + south.b, 0.05f, "row 0 is the south pole");
            // Kept in sRGB like every surface colour (the shader decodes it once): 128/255 stays ≈ 0.502.
            var grey = SurfaceAlbedoMap.FromSrgb32(2, 2, new byte[] { 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255 });
            Assert.AreEqual(0.502f, grey.Sample(Vector3d.UnitX).g, 0.002f);
            Assert.Less(map.SmoothBrightness(new Vector3d(0, 0, -1)), map.SmoothBrightness(new Vector3d(0, 0, 1)), "dark south is a basin");
        }

        [Test]
        public void AlbedoMap_ColoursTheSurface_AndFallsBackWhenMissing()
        {
            CelestialBodyPreset k = Body(HomeSystemCatalog.KolimpriId);
            var settings = k.Generation.Clone();
            settings.albedoMap = TestKey;
            settings.albedoMapDetail = 0.0;
            settings.craterCount = 0;

            var procedural = PlanetSurfaceSampler.Create(settings, k.Physical.meanRadius);
            Assert.IsFalse(procedural.HasAlbedoMap, "not registered → procedural colours");

            SurfaceAlbedoMaps.Register(TestKey, TwoColourMap());
            var mapped = PlanetSurfaceSampler.Create(settings, k.Physical.meanRadius);
            Assert.IsTrue(mapped.HasAlbedoMap);
            Vector3d westDir = PlanetRotation.DirectionFromLatLon(20.0 * MathD.DegToRad, -90.0 * MathD.DegToRad);
            ColorRGB c = mapped.Sample(westDir).Albedo;
            Assert.Greater(c.r, 0.9f);
            Assert.Less(c.b, 0.05f);
            Assert.AreNotEqual(PlanetFingerprint.Compute(procedural), PlanetFingerprint.Compute(mapped));
            Assert.AreEqual(PlanetFingerprint.Compute(mapped), PlanetFingerprint.Compute(PlanetSurfaceSampler.Create(settings, k.Physical.meanRadius)),
                "deterministic with the same map");

            // Relief from the map: the dark southern band is lower than the bright north (same noise elsewhere).
            Vector3d south = PlanetRotation.DirectionFromLatLon(-80.0 * MathD.DegToRad, 0.0), north = PlanetRotation.DirectionFromLatLon(80.0 * MathD.DegToRad, 0.0);
            double drop = (mapped.TerrainHeight(south) - procedural.TerrainHeight(south)) - (mapped.TerrainHeight(north) - procedural.TerrainHeight(north));
            Assert.Less(drop, -100.0);
            Assert.LessOrEqual(mapped.MinTerrainHeight, mapped.TerrainHeight(south));
            Assert.GreaterOrEqual(mapped.MaxTerrainHeight, mapped.TerrainHeight(north));
        }

        [Test]
        public void BodiesWithoutMapsOrCanyons_AreUnchanged()
        {
            // New optional fields default to "off": a surface built from settings that never mention them is identical.
            CelestialBodyPreset q = Body(HomeSystemCatalog.QuorisId);
            Assert.AreEqual("quoris", q.Generation.albedoMap, "every body has an artist map since catalog v5");
            var plain = q.Generation.Clone();
            plain.albedoMap = null; // Validate() turns null into "" — the field is "never mentioned"
            var explicitOff = q.Generation.Clone();
            explicitOff.albedoMap = "";
            explicitOff.featureCanyons = new FeatureCanyon[0];
            explicitOff.albedoMapRelief = 0.0;
            Assert.AreEqual(PlanetFingerprint.Compute(PlanetSurfaceSampler.Create(plain, q.Physical.meanRadius)),
                PlanetFingerprint.Compute(PlanetSurfaceSampler.Create(explicitOff, q.Physical.meanRadius)));
        }
    }
}
