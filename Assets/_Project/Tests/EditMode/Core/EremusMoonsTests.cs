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
    /// <summary>
    /// The eight moons of Eremus: designer data and its consistency (ρ, g, Kepler periods), orbits, spheres of influence,
    /// tidal locking, surface rules, Coloss' methane seas.
    /// </summary>
    public class EremusMoonsTests
    {
        const double Day = 86400.0;
        const double AU = PhysicalConstants.AstronomicalUnit;
        const double G = PhysicalConstants.GravitationalConstant;

        static CelestialBodyPreset Body(string id) => HomeSystemCatalog.Bodies().Find(b => b.Id == id);
        static CelestialBodyPreset Eremus => Body(HomeSystemCatalog.EremusId);
        static double EremusMu => Eremus.Physical.GravitationalParameter;

        // id, M [kg], R [m], ρ [kg/m³], g [m/s²], T [°C] — as quoted (Gandalf's mass: see the next test).
        [TestCase(HomeSystemCatalog.OriId, 1.37e20, 188335.0, 4908.0, 0.258, -135.0)]
        [TestCase(HomeSystemCatalog.OinId, 7.81e20, 344640.0, 4560.0, 0.439, -139.0)]
        [TestCase(HomeSystemCatalog.BifurId, 3.68e20, 271926.0, 4370.0, 0.332, -148.0)]
        [TestCase(HomeSystemCatalog.NevermourId, 1.86e21, 461554.0, 4510.0, 0.582, -155.0)]
        [TestCase(HomeSystemCatalog.DwalinId, 2.99e22, 1210924.0, 4020.0, 1.36, -152.0)]
        [TestCase(HomeSystemCatalog.BalinId, 3.72e22, 1266677.0, 4370.0, 1.55, -102.0)]
        [TestCase(HomeSystemCatalog.ColossId, 1.42e24, 4282711.0, 4310.0, 5.16, -179.0)]
        [TestCase(HomeSystemCatalog.GandalfId, HomeSystemCatalog.GandalfAssumedMass, 479950.0, 5960.0, 0.798, -189.0)]
        public void Moons_MatchDesignData(string id, double mass, double radius, double density, double g, double temperatureC)
        {
            CelestialBodyPreset b = Body(id);
            Assert.IsNotNull(b, id);
            Assert.AreEqual(HomeSystemCatalog.EremusId, b.ParentId, "orbits Eremus");
            Assert.AreEqual(mass, b.Physical.mass, mass * 1e-9);
            Assert.AreEqual(radius, b.Physical.meanRadius, 1e-6);
            // The quoted ρ and g follow from M and R within 0.5 % (largest: Gandalf's ρ, 5938 vs 5960, from the assumed mass).
            Assert.AreEqual(density, b.Physical.MeanDensity, density * 0.005, "ρ = M / (4/3·π·R³)");
            Assert.AreEqual(g, b.Physical.SurfaceGravity, g * 0.005, "g = GM/R²");
            Assert.AreEqual(temperatureC, b.Physical.atmosphere.surfaceTemperature - 273.15, 1e-9);
            Assert.IsEmpty(b.Physical.Validate());
        }

        [Test]
        public void Gandalf_QuotedMassIsATypo_TheAssumedMassFollowsFromGravityAndDensity()
        {
            const double r = 479950.0;
            double fromGravity = 0.798 * r * r / G;
            double fromDensity = 5960.0 * 4.0 / 3.0 * Math.PI * r * r * r;
            Assert.AreEqual(2.754e21, fromGravity, 0.001e21);
            Assert.AreEqual(2.760e21, fromDensity, 0.001e21);
            Assert.AreEqual(fromGravity, HomeSystemCatalog.GandalfAssumedMass, fromGravity * 0.003);
            Assert.Greater(HomeSystemCatalog.GandalfAssumedMass / 4.59e-4, 1e24, "the quoted 4.59·10⁻⁴ kg is off by ~25 orders of magnitude");
        }

        // id, a, pericentre, apocentre [m], inclination [deg], quoted orbital period [d], allowed period mismatch.
        [TestCase(HomeSystemCatalog.OriId, 2.44e8, 2.41e8, 2.47e8, 4.17, 0.474, 0.005)]          // Kepler 0.472 d (−0.4 %)
        [TestCase(HomeSystemCatalog.OinId, 0.00214 * AU, 0.00210 * AU, 0.00219 * AU, 4.18, 0.711, 0.003)]  // 0.710 d
        [TestCase(HomeSystemCatalog.BifurId, 0.0026 * AU, 0.00254 * AU, 0.00266 * AU, 4.17, 0.95, 0.003)]   // 0.951 d
        [TestCase(HomeSystemCatalog.NevermourId, 0.00329 * AU, 0.00328 * AU, 0.00329 * AU, 4.16, 1.35, 0.003)] // 1.353 d
        [TestCase(HomeSystemCatalog.DwalinId, 0.00482 * AU, 0.00475 * AU, 0.00489 * AU, 4.16, 2.40, 0.003)]  // 2.399 d
        [TestCase(HomeSystemCatalog.BalinId, 0.00646 * AU, 0.00641 * AU, 0.00651 * AU, 4.15, 3.73, 0.003)]  // 3.723 d
        [TestCase(HomeSystemCatalog.ColossId, 0.012 * AU, 0.0117 * AU, 0.0123 * AU, 4.13, 9.42, 0.003)]     // 9.425 d
        [TestCase(HomeSystemCatalog.GandalfId, 0.0144 * AU, 0.0137 * AU, 0.0152 * AU, 8.14, 12.4, 0.003)]  // 12.39 d
        public void Orbits_KeplerWithEremusMass_ReproducesTheQuotedPeriod_AndTheApsides(string id, double a, double peri, double apo,
            double inclination, double periodDays, double tolerance)
        {
            CelestialBodyPreset b = Body(id);
            Assert.AreEqual(a, b.Orbit.semiMajorAxis, 1e-6, "the designer's a is kept");
            Assert.AreEqual(0.0, b.Orbit.period, "the period is not an input: Kepler with Eremus' μ");
            var orbit = new KeplerOrbit(b.Orbit, EremusMu);
            Assert.AreEqual(periodDays * Day, orbit.Period, periodDays * Day * tolerance, "Eremus' mass reproduces the quoted period");

            double rMin = double.MaxValue, rMax = 0.0;
            for (int i = 0; i < 2000; i++)
            {
                double r = orbit.Position(orbit.Period * i / 2000.0).Magnitude;
                rMin = Math.Min(rMin, r);
                rMax = Math.Max(rMax, r);
            }
            // e comes from the apsides, a from the designer: (peri + apo)/2 differs from a by up to 0.35 % (Gandalf).
            Assert.AreEqual(peri, rMin, peri * 0.004, "pericentre");
            Assert.AreEqual(apo, rMax, apo * 0.004, "apocentre");
            Assert.AreEqual((apo - rMin) - (peri - rMin), apo - peri, 1e-3);

            Vector3d normal = Vector3d.Cross(orbit.Position(0.0), orbit.Velocity(0.0)).Normalized;
            Assert.AreEqual(inclination, Math.Acos(normal.z) * MathD.RadToDeg, 1e-6, "inclination to the ecliptic");
        }

        [Test]
        public void RegularMoons_OrbitInEremusEquatorialPlane_GandalfIsInclined()
        {
            CelestialBodyPreset e = Eremus;
            var spin = new PlanetRotation(e.Physical.siderealRotationPeriod, e.Physical.axialTiltDegrees * MathD.DegToRad, 0.0).SpinAxis;
            foreach (string id in HomeSystemCatalog.EremusMoonIds)
            {
                var orbit = new KeplerOrbit(Body(id).Orbit, EremusMu);
                Vector3d normal = Vector3d.Cross(orbit.Position(0.0), orbit.Velocity(0.0)).Normalized;
                double offset = Math.Acos(MathD.Clamp(Vector3d.Dot(normal, spin), -1.0, 1.0)) * MathD.RadToDeg;
                if (id == HomeSystemCatalog.GandalfId) Assert.AreEqual(4.0, offset, 0.1, "Gandalf: ≈ 4° out of the equator");
                else Assert.Less(offset, 0.05, id + " orbits in the equatorial plane");
            }
        }

        [Test]
        public void Moons_AreOrderedOutward_TheirSpheresOfInfluenceNeverOverlap_AndLieInsideEremusSphere()
        {
            var sys = PatchedConicSystem.FromHomeSystem();
            int eremus = sys.IndexOf(HomeSystemCatalog.EremusId);
            NavBody prev = null;
            foreach (string id in HomeSystemCatalog.EremusMoonIds)
            {
                int i = sys.IndexOf(id);
                Assert.GreaterOrEqual(i, 0, id);
                NavBody m = sys[i];
                Assert.AreEqual(eremus, m.ParentIndex, id);
                double peri = m.Orbit.SemiMajorAxis * (1.0 - m.Orbit.Eccentricity), apo = m.Orbit.SemiMajorAxis * (1.0 + m.Orbit.Eccentricity);
                Assert.Greater(m.SoiRadius, 3.0 * m.Radius - 1.0, id + ": room to approach and orbit");
                Assert.Less(apo + m.SoiRadius, 0.15 * sys[eremus].SoiRadius, id + " deep inside Eremus' SOI");
                Assert.Greater(peri - m.SoiRadius, sys[eremus].ZoneRadius, id + " above Eremus' numeric zone (air)");
                // Fluid Roche limit 2.44·R·(ρ_planet/ρ_moon)^(1/3): every moon is well outside it.
                double roche = 2.44 * sys[eremus].Radius * Math.Pow(Eremus.Physical.MeanDensity / m.Physical.MeanDensity, 1.0 / 3.0);
                Assert.Greater(peri, 1.4 * roche, id + " outside the Roche limit");
                if (prev != null)
                {
                    double prevApo = prev.Orbit.SemiMajorAxis * (1.0 + prev.Orbit.Eccentricity);
                    Assert.Less(prevApo + prev.SoiRadius, peri - m.SoiRadius, prev.Id + " and " + id + ": spheres never touch");
                }
                prev = m;
            }
            // Coloss is the big one: its sphere is ≈ 16 radii (6.8·10⁷ m), still 1.3·10⁸ m clear of Gandalf's at closest.
            NavBody coloss = sys[sys.IndexOf(HomeSystemCatalog.ColossId)];
            Assert.That(coloss.SoiRadius / coloss.Radius, Is.InRange(14.0, 18.0));
        }

        [Test]
        public void LockedMoons_KeepThePrimeMeridianTowardsEremus_GandalfRotatesFreely()
        {
            foreach (string id in HomeSystemCatalog.EremusMoonIds)
            {
                CelestialBodyPreset b = Body(id);
                var orbit = new KeplerOrbit(b.Orbit, EremusMu);
                var rot = new PlanetRotation(b.Physical.siderealRotationPeriod, b.Physical.axialTiltDegrees * MathD.DegToRad,
                    b.Physical.rotationPhaseAtEpochDegrees * MathD.DegToRad);
                Vector3d normal = Vector3d.Cross(orbit.Position(0.0), orbit.Velocity(0.0)).Normalized;
                Assert.Greater(Vector3d.Dot(rot.SpinAxis, normal), Math.Cos(1e-6), id + ": spin axis perpendicular to the orbit");
                if (id == HomeSystemCatalog.GandalfId)
                {
                    Assert.IsFalse(b.Orbit.tidallyLocked);
                    Assert.AreEqual(0.481 * Day, b.Physical.siderealRotationPeriod, 1e-6);
                    continue;
                }
                Assert.IsTrue(b.Orbit.tidallyLocked, id);
                Assert.AreEqual(orbit.Period, b.Physical.siderealRotationPeriod, 1e-6, id);
                // Optical libration on an eccentric orbit stays within ±2e (≤ 2.9° for Coloss).
                double maxLibration = 2.0 * b.Orbit.eccentricity + 1e-3;
                foreach (double f in new[] { 0.0, 0.13, 0.37, 0.5, 0.71, 0.94 })
                {
                    double t = f * orbit.Period;
                    Vector3d toPlanet = (-orbit.Position(t)).Normalized;
                    Assert.Greater(Vector3d.Dot(toPlanet, rot.FixedToInertial(Vector3d.UnitX, t)), Math.Cos(maxLibration), id + " at " + f);
                }
            }
        }

        [Test]
        public void Pads_AreOnTheNearSide_InDaylightAtTheStart()
        {
            var sys = PatchedConicSystem.FromHomeSystem();
            foreach (string id in HomeSystemCatalog.EremusMoonIds)
            {
                CelestialBodyPreset b = Body(id);
                int i = sys.IndexOf(id);
                var rot = new PlanetRotation(b.Physical.siderealRotationPeriod, b.Physical.axialTiltDegrees * MathD.DegToRad,
                    b.Physical.rotationPhaseAtEpochDegrees * MathD.DegToRad);
                Vector3d up = rot.FixedToInertial(PlanetRotation.DirectionFromLatLon(b.LaunchLatitude * MathD.DegToRad, b.LaunchLongitude * MathD.DegToRad), 0.0);
                Vector3d toStar = (-sys.Position(i, 0.0)).Normalized;
                Assert.Greater(Vector3d.Dot(up, toStar), 0.4, id + ": the sun is up (≈ 10:00)");
                if (b.Orbit.tidallyLocked)
                {
                    Vector3d toEremus = (sys.Position(sys.IndexOf(HomeSystemCatalog.EremusId), 0.0) - sys.Position(i, 0.0)).Normalized;
                    Assert.Greater(Vector3d.Dot(up, toEremus), 0.4, id + ": Eremus hangs in the sky over the pad");
                }
            }
        }

        [Test]
        public void SurfaceRules_RadiationBeltsBlockTheSuit_TheRobotMayGoOutEverywhere()
        {
            foreach (string id in HomeSystemCatalog.EremusMoonIds)
            {
                CelestialBodyPreset b = Body(id);
                bool inBelts = b.Orbit.semiMajorAxis < 15.0 * Eremus.Physical.meanRadius;
                Assert.AreEqual(inBelts ? 2 : 1, b.Hazards.radiationLevel, id);
                Assert.AreEqual(0, b.Hazards.acidLevel, id);
                Assert.AreEqual(id == HomeSystemCatalog.ColossId, b.Physical.atmosphere.hasAtmosphere, id + ": only Coloss has air");
                Assert.IsTrue(RobotEva.CanExit(b.Physical, b.Hazards).Ok, id + ": robot " + RobotEva.CanExit(b.Physical, b.Hazards).Reason);
                bool suit = SampleCollection.CanExit(b.Physical, b.Hazards).Ok;
                // Suit: radiation level 2 blocks the six inner moons; Gandalf (−189 °C) is below the suit's −183 °C.
                Assert.AreEqual(id == HomeSystemCatalog.ColossId, suit, id + ": suit");
                Assert.IsNotEmpty(SampleSiteGenerator.Around(id, b.Physical.meanRadius, Vector3d.UnitX), id + " has samples");
            }
        }

        [Test]
        public void Coloss_HasNitrogenAir_ThatKeepsMethaneLiquid()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.ColossId);
            AtmosphereProperties a = b.Physical.atmosphere;
            StringAssert.Contains("N₂", a.composition);
            Assert.AreEqual(94.15, a.surfaceTemperature, 1e-9);
            // Methane: triple point 90.7 K / 11.7 kPa; Clausius–Clapeyron (L = 8.19 kJ/mol) gives ≈ 17 kPa at 94 K.
            double pVap = 11.7e3 * Math.Exp(-8190.0 / 8.314462618 * (1.0 / a.surfaceTemperature - 1.0 / 90.7));
            Assert.That(pVap, Is.InRange(1.5e4, 2.0e4));
            Assert.Greater(a.surfaceTemperature, 90.7, "above methane's freezing point");
            Assert.Greater(a.surfacePressure, 5.0 * pVap, "the seas do not boil");
            // H = RT/(Mg) ≈ 5.5 km; the air is kept: Jeans λ for N₂ at a 150 K exobase ≫ 20.
            Assert.AreEqual(8.314462618 * 94.15 / (a.meanMolarMass * b.Physical.SurfaceGravity), a.scaleHeight, 1.0);
            Assert.That(a.scaleHeight, Is.InRange(5000.0, 6000.0));
            double vEsc = Math.Sqrt(2.0 * b.Physical.GravitationalParameter / b.Physical.meanRadius);
            double vTh = Math.Sqrt(2.0 * 1.380649e-23 * 150.0 / (0.028013 / 6.02214076e23));
            Assert.Greater(vEsc * vEsc / (vTh * vTh), 100.0);
            var profile = AtmosphericModel.Create(a);
            Assert.AreEqual(1.5e5, profile.Pressure(0.0), 1e-6);
            Assert.Less(profile.Density(b.Physical.spaceBoundaryAltitude) / profile.ReferenceDensity, 1e-7, "space starts ≈ 18 scale heights up");
        }

        [Test]
        public void Coloss_TealMethaneSeasCoverMostOfTheSurface_ThePadIsOnLand()
        {
            CelestialBodyPreset b = Body(HomeSystemCatalog.ColossId);
            Assert.IsTrue(b.Generation.hasOcean);
            var sampler = PlanetSurfaceSampler.Create(b.Generation, b.Physical.meanRadius);
            var rnd = new DeterministicRandom(23);
            int sea = 0, n = 3000;
            for (int i = 0; i < n; i++)
            {
                SurfaceSample s = sampler.Sample(rnd.UnitVector());
                if (!s.IsWater) continue;
                sea++;
                Assert.Less(Luminance(s.Albedo), 0.45, "open liquid, not ice");
                if (s.TerrainHeight < b.Generation.seaLevel - 1500.0)
                {
                    Assert.Greater(s.Albedo.g, 2.0 * s.Albedo.r, "deep methane is teal: red absorbed");
                    Assert.Greater(s.Albedo.b, 2.0 * s.Albedo.r);
                }
            }
            Assert.That(sea / (double)n, Is.InRange(0.55, 0.8), "the reference photo is mostly sea");
            Vector3d pad = PlanetRotation.DirectionFromLatLon(b.LaunchLatitude * MathD.DegToRad, b.LaunchLongitude * MathD.DegToRad);
            Assert.Greater(sampler.TerrainHeight(pad), b.Generation.seaLevel, "the pad is on land");
            var sites = SampleSiteGenerator.Around(HomeSystemCatalog.ColossId, b.Physical.meanRadius, pad, d => true);
            Assert.IsNotEmpty(sites);
            foreach (SampleSite s in sites) Assert.AreEqual(SampleCategory.OtherLiquid, s.Category, "liquid methane, not water");
        }

        [Test]
        public void Surfaces_AreCrateredRockWithTheReferenceColours()
        {
            foreach (string id in HomeSystemCatalog.EremusMoonIds)
            {
                CelestialBodyPreset b = Body(id);
                Assert.AreEqual(SurfaceStyle.Rocky, b.Generation.surfaceStyle, id);
                Assert.AreEqual(id, b.Generation.albedoMap, id + ": artist map since catalog v5 (procedural colours without it)");
                var sampler = PlanetSurfaceSampler.Create(b.Generation, b.Physical.meanRadius);
                Assert.Less(sampler.MaxSurfaceHeight - sampler.MinSurfaceHeight, 0.035 * b.Physical.meanRadius, id + ": a sphere, not a potato");
                ColorRGB mean = MeanLandAlbedo(sampler);
                Assert.Greater(mean.r, mean.b, id + ": warm-toned (no blue rock)");
            }
            Assert.Greater(MeanLandAlbedo(Sampler(HomeSystemCatalog.OinId)).r, 1.6 * MeanLandAlbedo(Sampler(HomeSystemCatalog.OinId)).g, "Oin is rust-red");
            ColorRGB gandalf = MeanLandAlbedo(Sampler(HomeSystemCatalog.GandalfId));
            Assert.Greater(gandalf.g, 0.9 * gandalf.r, "Gandalf is yellow-green");
        }

        static PlanetSurfaceSampler Sampler(string id)
        {
            CelestialBodyPreset b = Body(id);
            return PlanetSurfaceSampler.Create(b.Generation, b.Physical.meanRadius);
        }

        static ColorRGB MeanLandAlbedo(PlanetSurfaceSampler sampler)
        {
            var rnd = new DeterministicRandom(41);
            double r = 0, g = 0, bl = 0;
            int n = 0;
            for (int i = 0; i < 800; i++)
            {
                SurfaceSample s = sampler.Sample(rnd.UnitVector());
                if (s.IsWater) continue;
                r += s.Albedo.r;
                g += s.Albedo.g;
                bl += s.Albedo.b;
                n++;
            }
            return new ColorRGB((float)(r / n), (float)(g / n), (float)(bl / n));
        }

        static double Luminance(ColorRGB c) => 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
    }
}
