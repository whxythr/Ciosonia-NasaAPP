using System;
using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Units;

namespace SpaceSim.Tests
{
    /// <summary>
    /// The designer's orbits (2026-11-15): apsides, semi-major axes and inclinations of the planets and the moons of Ciosonia,
    /// Hermeous Mora and Tholin, the tidal locks on the now eccentric, inclined orbits, and the double planet Mocha-Mocha + Dott.
    /// </summary>
    public class HomeOrbitsTests
    {
        const double Day = 86400.0;
        const double AU = PhysicalConstants.AstronomicalUnit;
        const double G = PhysicalConstants.GravitationalConstant;

        static CelestialBodyPreset Body(string id) => HomeSystemCatalog.Bodies().Find(b => b.Id == id);
        static double StarMu => HomeSystemCatalog.Star().GravitationalParameter;

        static double ParentMu(CelestialBodyPreset b) =>
            b.ParentId == null ? StarMu : Body(b.ParentId).Physical.GravitationalParameter;

        /// <summary>Inclination of the orbit plane to the reference (XY) plane, from the rails themselves [deg].</summary>
        static double MeasuredInclination(KeplerOrbit orbit, double t = 0.0) =>
            Math.Acos(Vector3d.Cross(orbit.Position(t), orbit.Velocity(t)).Normalized.z) * MathD.RadToDeg;

        // id, pericentre, apocentre, semi-major axis (in units of `unit` metres), inclination [deg], unit [m]
        [TestCase(HomeSystemCatalog.TeonanId, 0.0858, 0.0908, 0.0883, 3.17, AU)]
        [TestCase(HomeSystemCatalog.QuorisId, 0.124, 0.132, 0.128, 3.17, AU)]
        [TestCase(HomeSystemCatalog.HermeousMoraId, 0.411, 0.442, 0.427, 3.17, AU)]
        [TestCase(HomeSystemCatalog.HomeWorldId, 0.63, 0.71, 0.67, 3.17, AU)]
        [TestCase(HomeSystemCatalog.TholinId, 0.798, 0.931, 0.865, 3.17, AU)]
        [TestCase(HomeSystemCatalog.EremusId, 1.98, 2.12, 2.05, 3.17, AU)]
        [TestCase(HomeSystemCatalog.MochaMochaId, 4.23, 4.50, 4.36, 5.46, AU)]
        [TestCase(HomeSystemCatalog.MoraMoonId, 18280005.0, 37311552.0, 27795780.0, 4.37, 1.0)]
        [TestCase(HomeSystemCatalog.KolimpriId, 54684487.0, 59092271.0, 56888489.0, 6.51, 1.0)]
        [TestCase(HomeSystemCatalog.TromosId, 84676534.0, 109000000.0, 96890115.0, 3.58, 1.0)]
        [TestCase(HomeSystemCatalog.LinId, 44991524.0, 52279669.0, 48635612.0, 1.26, 1.0)]
        [TestCase(HomeSystemCatalog.TiafoeId, 19173309.0, 29635390.0, 24405443.0, 40.4, 1.0)]
        public void Orbit_MatchesTheDesignerData(string id, double peri, double apo, double a, double inclination, double unit)
        {
            CelestialBodyPreset b = Body(id);
            Assert.IsNotNull(b, id);
            double e = (apo - peri) / (apo + peri);
            Assert.AreEqual(a * unit, b.Orbit.semiMajorAxis, a * unit * 1e-12, "a as quoted");
            Assert.AreEqual(e, b.Orbit.eccentricity, 1e-12, "e = (Da − Dp)/(Da + Dp)");
            Assert.AreEqual(inclination, b.Orbit.inclinationDegrees, 1e-12);
            Assert.AreEqual(0.0, b.Orbit.longitudeOfAscendingNodeDegrees, "nodes not given: Ω = 0");

            // The rails really fly this ellipse: radius range and the plane, measured from KeplerOrbit itself.
            var orbit = new KeplerOrbit(b.Orbit, ParentMu(b));
            double rMin = double.MaxValue, rMax = 0.0, zMax = 0.0;
            for (int k = 0; k < 4000; k++)
            {
                Vector3d r = orbit.Position(orbit.Period * k / 4000.0);
                rMin = Math.Min(rMin, r.Magnitude);
                rMax = Math.Max(rMax, r.Magnitude);
                zMax = Math.Max(zMax, Math.Abs(r.z) / r.Magnitude);
            }
            // The quoted a is used, so the apsides agree with the quoted ones to the rounding of the data (Tromos' Da: 0.06 %).
            Assert.AreEqual(peri * unit, rMin, peri * unit * 0.0015, "pericentre");
            Assert.AreEqual(apo * unit, rMax, apo * unit * 0.0015, "apocentre");
            Assert.AreEqual(inclination, MeasuredInclination(orbit, 0.37 * orbit.Period), 1e-9, "orbit plane");
            Assert.AreEqual(Math.Sin(inclination * MathD.DegToRad), zMax, 2e-4, "leaves the reference plane by r·sin i");
        }

        [TestCase(HomeSystemCatalog.TeonanId, 5.94)]
        [TestCase(HomeSystemCatalog.QuorisId, 10.4)]
        [TestCase(HomeSystemCatalog.HermeousMoraId, 63.1)]
        [TestCase(HomeSystemCatalog.HomeWorldId, 124.0)]
        [TestCase(HomeSystemCatalog.TholinId, 182.0)]
        [TestCase(HomeSystemCatalog.EremusId, 664.0)]
        public void QuotedPlanetPeriods_AreKept_AndAgreeWithKepler(string id, double periodDays)
        {
            CelestialBodyPreset b = Body(id);
            var orbit = new KeplerOrbit(b.Orbit, StarMu);
            Assert.AreEqual(periodDays * Day, orbit.Period, 1e-6);
            Assert.AreEqual(periodDays * Day, OrbitalMechanics.CircularPeriod(StarMu, b.Orbit.semiMajorAxis), periodDays * Day * 0.005,
                "Kepler with the star's mass agrees within 0.5 %");
        }

        [Test]
        public void Planets_ShareOnePlane_TheDoublePlanetIsTilted()
        {
            var sys = PatchedConicSystem.FromHomeSystem();
            Vector3d Normal(string id)
            {
                int i = sys.IndexOf(id);
                return Vector3d.Cross(sys[i].Orbit.Position(0.0), sys[i].Orbit.Velocity(0.0)).Normalized;
            }
            Vector3d home = Normal(HomeSystemCatalog.HomeWorldId);
            foreach (string id in new[] { HomeSystemCatalog.TeonanId, HomeSystemCatalog.QuorisId, HomeSystemCatalog.HermeousMoraId,
                         HomeSystemCatalog.TholinId, HomeSystemCatalog.EremusId })
                Assert.Greater(Vector3d.Dot(Normal(id), home), Math.Cos(1e-9), id + ": same plane as the start planet");
            double mutual = Math.Acos(Vector3d.Dot(Normal(HomeSystemCatalog.MochaMochaId), home)) * MathD.RadToDeg;
            Assert.AreEqual(5.46 - 3.17, mutual, 1e-6, "the double planet is 2.29° out of the planets' plane");

            // Star-centric positions (what the simulation and the map draw) leave the reference plane: Tholin's height above it
            // swings between ±r·sin 3.17°.
            int t = sys.IndexOf(HomeSystemCatalog.TholinId);
            double zMin = double.MaxValue, zMax = double.MinValue;
            for (int k = 0; k < 400; k++)
            {
                Vector3d p = sys.Position(t, sys[t].Orbit.Period * k / 400.0);
                zMin = Math.Min(zMin, p.z / p.Magnitude);
                zMax = Math.Max(zMax, p.z / p.Magnitude);
            }
            Assert.AreEqual(Math.Sin(3.17 * MathD.DegToRad), zMax, 1e-4);
            Assert.AreEqual(-Math.Sin(3.17 * MathD.DegToRad), zMin, 1e-4);
        }

        [TestCase(HomeSystemCatalog.HomeWorldId, 22.5)]
        [TestCase(HomeSystemCatalog.TholinId, 30.0)]
        [TestCase(HomeSystemCatalog.HermeousMoraId, 2.6)]
        [TestCase(HomeSystemCatalog.TeonanId, 0.6)]
        public void Obliquity_IsMeasuredFromTheBodysOwnOrbit(string id, double obliquity)
        {
            CelestialBodyPreset b = Body(id);
            var orbit = new KeplerOrbit(b.Orbit, StarMu);
            var rot = new PlanetRotation(b.Physical.siderealRotationPeriod, b.Physical.axialTiltDegrees * MathD.DegToRad, 0.0);
            Vector3d normal = Vector3d.Cross(orbit.Position(0.0), orbit.Velocity(0.0)).Normalized;
            Assert.AreEqual(obliquity, Math.Acos(Vector3d.Dot(rot.SpinAxis, normal)) * MathD.RadToDeg, 1e-6);
        }

        [TestCase(HomeSystemCatalog.QuorisId)]
        [TestCase(HomeSystemCatalog.MoraMoonId)]
        [TestCase(HomeSystemCatalog.KolimpriId)]
        [TestCase(HomeSystemCatalog.LinId)]
        [TestCase(HomeSystemCatalog.TiafoeId)]
        [TestCase(HomeSystemCatalog.DottId)]
        public void TidalLock_StaysInPhase_OnEccentricInclinedOrbits(string id)
        {
            CelestialBodyPreset b = Body(id);
            Assert.IsTrue(b.Orbit.tidallyLocked, id);
            var orbit = new KeplerOrbit(b.Orbit, ParentMu(b));
            double T = orbit.Period;
            Assert.AreEqual(T, b.Physical.siderealRotationPeriod, T * 1e-12, "rotation = revolution, exactly");
            var rot = new PlanetRotation(b.Physical.siderealRotationPeriod, b.Physical.axialTiltDegrees * MathD.DegToRad,
                b.Physical.rotationPhaseAtEpochDegrees * MathD.DegToRad);
            Vector3d normal = Vector3d.Cross(orbit.Position(0.0), orbit.Velocity(0.0)).Normalized;
            Assert.Greater(Vector3d.Dot(rot.SpinAxis, normal), Math.Cos(1e-9), "spin axis = orbit normal");

            // The prime meridian turns with the mean anomaly: it points exactly at the parent at every pericentre and apocentre,
            // also many orbits later (no drift).
            double tPeri = ((360.0 - b.Orbit.meanAnomalyAtEpochDegrees) % 360.0 + 360.0) % 360.0 / 360.0 * T;
            foreach (double t in new[] { tPeri, tPeri + 0.5 * T, tPeri + 1000.0 * T, tPeri + 1000.5 * T })
            {
                Vector3d toParent = (-orbit.Position(t)).Normalized;
                Assert.Greater(Vector3d.Dot(toParent, rot.FixedToInertial(Vector3d.UnitX, t)), Math.Cos(1e-6), id + " at an apsis");
            }
            // In between, the parent librates by at most ν − M ≤ 2e + 1.25e² + 1.5e³ (series bound: 51° for Aegis, e = 0.34, whose
            // actual maximum is 39.8°).
            double e = b.Orbit.eccentricity;
            double bound = 2.0 * e + 1.25 * e * e + 1.5 * e * e * e + 1e-6;
            for (int k = 0; k < 200; k++)
            {
                double t = T * k / 200.0;
                Vector3d toParent = (-orbit.Position(t)).Normalized;
                Assert.Greater(Vector3d.Dot(toParent, rot.FixedToInertial(Vector3d.UnitX, t)), Math.Cos(bound), id + " at " + k);
            }
        }

        [Test]
        public void TrueAnomaly_SolvesKeplersEquation()
        {
            Assert.AreEqual(0.0, HomeSystemCatalog.TrueAnomalyDegrees(0.0, 0.3), 1e-12);
            Assert.AreEqual(180.0, HomeSystemCatalog.TrueAnomalyDegrees(180.0, 0.3), 1e-9);
            Assert.AreEqual(40.0, HomeSystemCatalog.TrueAnomalyDegrees(40.0, 0.0), 1e-12);
            // Against the rails: the direction angle in the orbit plane at t = 0.
            var orbit = new KeplerOrbit(new OrbitProperties { semiMajorAxis = 1e10, eccentricity = 0.2, period = 1000.0, meanAnomalyAtEpochDegrees = 70.0 }, 1.0);
            Vector3d p = orbit.Position(0.0);
            Assert.AreEqual(Math.Atan2(p.y, p.x) * MathD.RadToDeg, HomeSystemCatalog.TrueAnomalyDegrees(70.0, 0.2), 1e-9);
        }

        // ---- Mocha-Mocha + Dott ----

        [TestCase(HomeSystemCatalog.DottId, 7.95e22, 1644016.0, 4270.0, 1.96, -202.0, 5.39)]
        [TestCase(HomeSystemCatalog.MochaMochaId, 1.65e23, 2034254.0, 4680.0, 2.66, -249.0, 26.0)]
        public void DoublePlanet_MatchesDesignData(string id, double mass, double radius, double density, double g, double tempC, double dayDays)
        {
            CelestialBodyPreset b = Body(id);
            Assert.IsNotNull(b, id);
            Assert.AreEqual(mass, b.Physical.mass, mass * 1e-12);
            Assert.AreEqual(radius, b.Physical.meanRadius, 1e-6);
            Assert.AreEqual(density, b.Physical.MeanDensity, density * 0.002, "ρ = M / (4/3·π·R³)");
            Assert.AreEqual(g, b.Physical.SurfaceGravity, g * 0.003, "g = GM/R²");
            Assert.AreEqual(tempC, b.Physical.atmosphere.surfaceTemperature - 273.15, 1e-9);
            Assert.AreEqual(dayDays * Day, b.Physical.siderealRotationPeriod, 1e-6);
            Assert.IsFalse(b.Physical.atmosphere.hasAtmosphere, "no atmosphere data → airless");
            Assert.IsEmpty(b.Physical.Validate());
            Assert.IsNotEmpty(b.Description);
            Assert.AreEqual(id, b.Generation.albedoMap, "named artist map slot (procedural until it is drawn)");
        }

        [Test]
        public void DoublePlanet_IsAKeplerConsistentPair()
        {
            CelestialBodyPreset mocha = Body(HomeSystemCatalog.MochaMochaId), dott = Body(HomeSystemCatalog.DottId);
            Assert.IsNull(mocha.ParentId, "Mocha-Mocha carries the barycentre's orbit around the star");
            Assert.AreEqual(HomeSystemCatalog.MochaMochaId, dott.ParentId);
            Assert.AreEqual(2.1, dott.Orbit.inclinationDegrees, 1e-12);
            Assert.AreEqual(0.0, dott.Orbit.eccentricity);

            // Dott's rails: the relative orbit with T = 5.39 d — the implied μ is that of the whole pair.
            var rel = new KeplerOrbit(dott.Orbit, mocha.Physical.GravitationalParameter);
            double muPair = G * (HomeSystemCatalog.MochaMochaMass + HomeSystemCatalog.DottMass);
            Assert.AreEqual(5.39 * Day, rel.Period, 1e-6);
            Assert.AreEqual(muPair, rel.ImpliedParentGravitationalParameter, muPair * 1e-12);
            double d = HomeSystemCatalog.DoublePlanetSeparation;
            Assert.AreEqual(44755e3, d, 1e3, "separation from Kepler's third law");
            Assert.AreEqual(d, rel.Position(12345.0).Magnitude, 1e-3, "circular");

            // Barycentre distances follow the mass ratio and add up to the separation; the barycentre is outside both bodies.
            double rM = HomeSystemCatalog.MochaMochaBarycentreDistance, rD = HomeSystemCatalog.DottBarycentreDistance;
            Assert.AreEqual(d, rM + rD, 1e-6);
            Assert.AreEqual(HomeSystemCatalog.DottMass / HomeSystemCatalog.MochaMochaMass, rM / rD, 1e-12);
            Assert.Greater(rM, mocha.Physical.meanRadius, "a true double planet (like Pluto–Charon)");

            // Physically sound: outside the fluid Roche limit, well inside the pair's Hill sphere.
            double roche = 2.44 * mocha.Physical.meanRadius * Math.Pow(mocha.Physical.MeanDensity / dott.Physical.MeanDensity, 1.0 / 3.0);
            Assert.Greater(d, 5.0 * roche);
            double hill = mocha.Orbit.semiMajorAxis * Math.Pow(muPair / (3.0 * StarMu), 1.0 / 3.0);
            Assert.Less(d, 0.05 * hill);
            // The barycentre's period around the star follows from Kepler (none was given): ≈ 2059 d ≈ 5.6 years.
            var helio = new KeplerOrbit(mocha.Orbit, StarMu);
            Assert.AreEqual(2059.0 * Day, helio.Period, 2.0 * Day);
        }

        [Test]
        public void DoublePlanet_SpheresOfInfluenceAreUsable()
        {
            var sys = PatchedConicSystem.FromHomeSystem();
            int m = sys.IndexOf(HomeSystemCatalog.MochaMochaId), d = sys.IndexOf(HomeSystemCatalog.DottId);
            Assert.GreaterOrEqual(m, 0);
            Assert.GreaterOrEqual(d, 0);
            Assert.AreEqual(PatchedConicSystem.Star, sys.Parent(m));
            Assert.AreEqual(m, sys.Parent(d));
            double sep = sys[d].Orbit.SemiMajorAxis;
            // Dott's sphere never reaches Mocha-Mocha's numeric zone, and the whole pair lies deep inside Mocha-Mocha's sphere.
            Assert.Greater(sep - sys[d].SoiRadius, 4.0 * sys[m].ZoneRadius);
            Assert.Less(sep + sys[d].SoiRadius, 0.2 * sys[m].SoiRadius);
            // A point just above Mocha-Mocha's far side belongs to Mocha-Mocha, one above Dott to Dott.
            Vector3d toDott = sys.Position(d, 0.0) - sys.Position(m, 0.0);
            Assert.AreEqual(m, sys.PrimaryAt(sys.Position(m, 0.0) - toDott.Normalized * (sys[m].Radius + 1e5), 0.0, PatchedConicSystem.Star));
            Assert.AreEqual(d, sys.PrimaryAt(sys.Position(d, 0.0) + toDott.Normalized * (sys[d].Radius + 1e5), 0.0, PatchedConicSystem.Star));
        }
    }
}
