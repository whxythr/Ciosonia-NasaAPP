using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Economy;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using SpaceSim.Units;

namespace SpaceSim.Tests
{
    /// <summary>The fifth and sixth planets: Tholin (CO₂ lakes, tholin north, icy south) and Eremus (orange mega-Jupiter).</summary>
    public class OuterPlanetsTests
    {
        const double Day = 86400.0;
        const double AU = PhysicalConstants.AstronomicalUnit;
        const double EarthMass = 5.9722e24, EarthRadius = 6371000.0;

        static CelestialBodyPreset Body(string id) => HomeSystemCatalog.Bodies().Find(b => b.Id == id);
        static double StarMu => PhysicalConstants.GravitationalConstant * HomeSystemCatalog.Star().mass;

        [Test]
        public void Tholin_MatchesDesignData()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.TholinId);
            Assert.IsNotNull(b);
            Assert.IsNull(b.ParentId, "orbits the star");
            Assert.AreEqual(9.36e24, b.Physical.mass, 1e15);
            Assert.AreEqual(7244420.0, b.Physical.meanRadius, 1e-6);
            Assert.AreEqual(5880.0, b.Physical.MeanDensity, 5880.0 * 0.005, "ρ = M / (4/3·π·R³)");
            Assert.AreEqual(11.9, b.Physical.SurfaceGravity, 11.9 * 0.005, "g = GM/R²");
            Assert.AreEqual(1.75 * Day, b.Physical.siderealRotationPeriod, 1e-6);
            Assert.AreEqual(37.8, b.Physical.atmosphere.surfaceTemperature - 273.15, 1e-9);
            Assert.IsEmpty(b.Physical.Validate());
        }

        [Test]
        public void Eremus_MatchesDesignData()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.EremusId);
            Assert.IsNotNull(b);
            Assert.IsNull(b.ParentId);
            Assert.AreEqual(864.0 * EarthMass, b.Physical.mass, 1e18);
            Assert.AreEqual(12.9 * EarthRadius, b.Physical.meanRadius, 1e-3);
            // The quoted ρ and g follow from M and R within 1 % (2219 vs 2208 kg/m³, 51.0 vs 50.8 m/s²).
            Assert.AreEqual(2208.0, b.Physical.MeanDensity, 2208.0 * 0.01);
            Assert.AreEqual(50.8, b.Physical.SurfaceGravity, 50.8 * 0.01);
            Assert.AreEqual(0.329 * Day, b.Physical.siderealRotationPeriod, 1e-6);
            Assert.AreEqual(-63.6, b.Physical.atmosphere.surfaceTemperature - 273.15, 1e-9);
            Assert.IsEmpty(b.Physical.Validate());
            // The largest object of the system: more massive and larger than every other body.
            foreach (CelestialBodyPreset other in HomeSystemCatalog.Bodies())
            {
                if (other.Id == b.Id) continue;
                Assert.Greater(b.Physical.mass, other.Physical.mass, other.Id);
                Assert.Greater(b.Physical.meanRadius, other.Physical.meanRadius, other.Id);
            }
        }

        [TestCase(HomeSystemCatalog.TholinId, 0.799, 0.931, 182.0)]
        [TestCase(HomeSystemCatalog.EremusId, 1.98, 2.12, 664.0)]
        public void Orbits_ReproduceTheApsidesAndThePeriod(string id, double periAU, double apoAU, double periodDays)
        {
            CelestialBodyPreset b = Body(id);
            var orbit = new KeplerOrbit(b.Orbit, StarMu);
            Assert.AreEqual(periodDays * Day, orbit.Period, 1e-6, "the given period is used");
            // Kepler's third law with the star's mass agrees with the given period (the star's mass comes from Hermeous Mora).
            double kepler = 2.0 * Math.PI * Math.Sqrt(Math.Pow(b.Orbit.semiMajorAxis, 3) / StarMu);
            Assert.AreEqual(periodDays * Day, kepler, periodDays * Day * 0.01);

            double rMin = double.MaxValue, rMax = 0.0;
            for (int i = 0; i < 2000; i++)
            {
                double r = orbit.Position(orbit.Period * i / 2000.0).Magnitude;
                rMin = Math.Min(rMin, r);
                rMax = Math.Max(rMax, r);
            }
            Assert.AreEqual(periAU * AU, rMin, periAU * AU * 0.002, "pericentre");
            Assert.AreEqual(apoAU * AU, rMax, apoAU * AU * 0.002, "apocentre");
        }

        [Test]
        public void NewPlanets_AreTheFifthAndSixth_AndTheirSpheresOfInfluenceDoNotOverlap()
        {
            CelestialBodyPreset home = Body(HomeSystemCatalog.HomeWorldId), tholin = Body(HomeSystemCatalog.TholinId),
                eremus = Body(HomeSystemCatalog.EremusId);
            double tholinPeri = tholin.Orbit.semiMajorAxis * (1.0 - tholin.Orbit.eccentricity);
            double tholinApo = tholin.Orbit.semiMajorAxis * (1.0 + tholin.Orbit.eccentricity);
            double eremusPeri = eremus.Orbit.semiMajorAxis * (1.0 - eremus.Orbit.eccentricity);
            Assert.Greater(tholinPeri, home.Orbit.semiMajorAxis, "outside the fourth planet");
            Assert.Less(tholinApo, eremusPeri, "inside the sixth");

            var sys = PatchedConicSystem.FromHomeSystem();
            int t = sys.IndexOf(HomeSystemCatalog.TholinId), e = sys.IndexOf(HomeSystemCatalog.EremusId), h = sys.IndexOf(HomeSystemCatalog.HomeWorldId);
            Assert.GreaterOrEqual(t, 0);
            Assert.GreaterOrEqual(e, 0);
            Assert.Less(sys[t].ParentIndex, 0, "Tholin orbits the star");
            Assert.Less(sys[e].ParentIndex, 0, "Eremus orbits the star");
            Assert.Less(home.Orbit.semiMajorAxis + sys[h].SoiRadius, tholinPeri - sys[t].SoiRadius);
            Assert.Less(tholinApo + sys[t].SoiRadius, eremusPeri - sys[e].SoiRadius);
            // A mega-Jupiter's sphere of influence is huge: a(m/M)^0.4 ≈ 1.9·10¹⁰ m ≈ 235 planet radii.
            Assert.That(sys[e].SoiRadius / sys[e].Radius, Is.InRange(150.0, 350.0));
        }

        [Test]
        public void Tholin_CO2CanBeLiquid_AndTheLakesFillAboutHalfTheSurface()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.TholinId);
            AtmosphereProperties a = b.Physical.atmosphere;
            StringAssert.Contains("CO₂", a.composition);
            // Liquid CO₂ needs a pressure between the triple point (5.18 bar) and the critical point (73.8 bar).
            Assert.That(a.surfacePressure, Is.InRange(5.18e5, 7.38e6));
            Assert.IsFalse(SampleCollection.CanExit(b.Physical, b.Hazards).Ok, "60 bar is above the suit limit");

            // Without the artist map (not registered in Core tests) the procedural relief still fills about half with lakes.
            Assert.IsTrue(b.Generation.hasOcean);
            var sampler = PlanetSurfaceSampler.Create(b.Generation, b.Physical.meanRadius);
            var rnd = new DeterministicRandom(5);
            int lake = 0, n = 3000;
            for (int i = 0; i < n; i++)
            {
                Vector3d d = rnd.UnitVector();
                SurfaceSample s = sampler.Sample(d);
                if (!s.IsWater) continue;
                lake++;
                Assert.AreEqual(b.Generation.seaLevel, s.SurfaceHeight, 1e-9, "flat lake surface");
                Assert.Less(s.TerrainHeight, b.Generation.seaLevel);
                Assert.Less(Luminance(s.Albedo), 0.45, "the shader shades it as open liquid, not ice");
            }
            Assert.That(lake / (double)n, Is.InRange(0.3, 0.7), "about half the surface is lake");
        }

        [Test]
        public void RockyLake_ShowsTheBedThroughTheLiquid_AndDarkensWithDepth()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.TholinId);
            var withLakes = PlanetSurfaceSampler.Create(b.Generation, b.Physical.meanRadius);
            var dry = b.Generation.Clone();
            dry.hasOcean = false;
            var withoutLakes = PlanetSurfaceSampler.Create(dry, b.Physical.meanRadius);
            var rnd = new DeterministicRandom(11);
            int checkedLakes = 0, checkedLand = 0;
            for (int i = 0; i < 2000; i++)
            {
                Vector3d d = rnd.UnitVector();
                SurfaceSample wet = withLakes.Sample(d), bed = withoutLakes.Sample(d);
                Assert.AreEqual(bed.TerrainHeight, wet.TerrainHeight, 1e-9, "the lakes change no terrain");
                if (!wet.IsWater)
                {
                    // Dry land is shaded exactly as on a body without lakes.
                    Assert.AreEqual(bed.Albedo.r, wet.Albedo.r, 0f);
                    Assert.AreEqual(bed.Albedo.g, wet.Albedo.g, 0f);
                    Assert.AreEqual(bed.Albedo.b, wet.Albedo.b, 0f);
                    checkedLand++;
                    continue;
                }
                Assert.Less(Luminance(wet.Albedo), Luminance(bed.Albedo) + 1e-6, "liquid over the bed is not brighter than the dry bed");
                checkedLakes++;
            }
            Assert.Greater(checkedLakes, 100);
            Assert.Greater(checkedLand, 100);
        }

        [Test]
        public void Eremus_HasNoSolidRelief_AndOrangeZonalBands()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.EremusId);
            Assert.AreEqual(SurfaceStyle.GasGiant, b.Generation.surfaceStyle);
            var sampler = PlanetSurfaceSampler.Create(b.Generation, b.Physical.meanRadius);
            Assert.AreEqual(0.0, sampler.MinSurfaceHeight, 1e-9);
            Assert.AreEqual(0.0, sampler.MaxSurfaceHeight, 1e-9);

            // Luminance profile: varies strongly from band to band (latitude), little along a band (longitude).
            const int lats = 90, lons = 90;
            var rowMean = new double[lats];
            double alongBand = 0.0;
            for (int i = 0; i < lats; i++)
            {
                double lat = (-80.0 + 160.0 * (i + 0.5) / lats) * MathD.DegToRad;
                var row = new double[lons];
                for (int j = 0; j < lons; j++)
                {
                    double lon = (-180.0 + 360.0 * j / lons) * MathD.DegToRad;
                    SurfaceSample s = sampler.Sample(PlanetRotation.DirectionFromLatLon(lat, lon));
                    Assert.IsFalse(s.IsWater);
                    Assert.AreEqual(0.0, s.TerrainHeight, 1e-9, "the 1-bar level is a smooth sphere");
                    Assert.Greater(s.Albedo.r, s.Albedo.g, "orange: red > green");
                    Assert.Greater(s.Albedo.g, s.Albedo.b, "orange: green > blue");
                    row[j] = Luminance(s.Albedo);
                    rowMean[i] += row[j] / lons;
                }
                foreach (double v in row) alongBand += (v - rowMean[i]) * (v - rowMean[i]) / (lats * lons);
            }
            double mean = 0.0, acrossBands = 0.0;
            foreach (double m in rowMean) mean += m / lats;
            foreach (double m in rowMean) acrossBands += (m - mean) * (m - mean) / lats;
            Assert.Greater(acrossBands, 2.0 * alongBand, "zonal bands: the colour changes with latitude more than along a band");
        }

        [Test]
        public void Eremus_IsNotSafeOutside_AndHasNothingToSample()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.EremusId);
            Assert.IsFalse(SampleCollection.CanExit(b.Physical, b.Hazards).Ok);
            var rnd = new DeterministicRandom(3);
            for (int k = 0; k < 20; k++)
                Assert.IsEmpty(SampleSiteGenerator.Around(HomeSystemCatalog.EremusId, b.Physical.meanRadius, rnd.UnitVector(), d => false));
        }

        [Test]
        public void Tholin_Samples_LiquidCO2FromLakes_TholinDustOnLand()
        {
            int liquid = 0, dust = 0;
            var rnd = new DeterministicRandom(17);
            for (int k = 0; k < 40; k++)
            {
                Vector3d d = rnd.UnitVector();
                List<SampleSite> sites = SampleSiteGenerator.Around(HomeSystemCatalog.TholinId, 7244420.0, d, x => x.z < 0.0);
                foreach (SampleSite s in sites)
                {
                    if (s.FixedDirection.z < 0.0)
                    {
                        Assert.AreEqual(SampleCategory.OtherLiquid, s.Category, "liquid CO₂, not water");
                        Assert.IsTrue(s.IsLiquid);
                        liquid++;
                    }
                    else
                    {
                        Assert.AreNotEqual(SampleCategory.Water, s.Category);
                        Assert.AreNotEqual(SampleCategory.Acid, s.Category);
                        if (s.Category == SampleCategory.Soil) dust++;
                    }
                }
            }
            Assert.Greater(liquid, 0);
            Assert.Greater(dust, 0);
        }

        static double Luminance(ColorRGB c) => 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
    }
}
