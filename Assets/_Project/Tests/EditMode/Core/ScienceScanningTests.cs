using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Economy;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using SpaceSim.Science;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    public class ScienceScanningTests
    {
        static ScannerSpec Spec(string id) => ScannerSpec.From(ModuleCatalog.Find(id));

        static PlanetModel Body(string id, out CelestialBodyPreset preset)
        {
            preset = HomeSystemCatalog.Bodies().Find(b => b.Id == id);
            Assert.IsNotNull(preset, id);
            var sampler = PlanetSurfaceSampler.Create(preset.Generation, preset.Physical.meanRadius);
            return new PlanetModel(preset.DisplayName, preset.Physical, sampler, Vector3d.Zero);
        }

        static PlanetScienceModel Science(string id)
        {
            PlanetModel m = Body(id, out CelestialBodyPreset p);
            return new PlanetScienceModel(id, m, SurfaceClimate.ReferenceFor(p.Physical.atmosphere, 0.0));
        }

        /// <summary>Circular equatorial-ish orbit state at time t (inclination <paramref name="incl"/>).</summary>
        static void Circular(PlanetModel m, double altitude, double incl, out Vector3d r, out Vector3d v)
        {
            double a = m.Radius + altitude, speed = Math.Sqrt(m.GravitationalParameter / a);
            r = new Vector3d(a, 0, 0);
            v = new Vector3d(0, speed * Math.Cos(incl), speed * Math.Sin(incl));
        }

        [Test]
        public void Catalog_HasThreeScannersWithCapabilities()
        {
            foreach (string id in new[] { ModuleCatalog.TemperatureRadar, ModuleCatalog.PressureRadar, ModuleCatalog.TerrainScanner })
            {
                ModuleDefinition m = ModuleCatalog.Find(id);
                Assert.IsNotNull(m, id);
                Assert.IsTrue(m.IsScanner);
                Assert.Greater(m.scanMaxAltitude, m.scanMinAltitude);
                Assert.Greater(m.scanIfov, 0.0);
                Assert.Greater(m.powerDraw, 0.0);
                Assert.Greater(m.dryMass, 0.0);
            }
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Add(ModuleCatalog.TemperatureRadar);
            b.payloadModules.Add(ModuleCatalog.TerrainScanner);
            CompiledRocket c = BlueprintCompiler.Compile(b, id => true);
            Assert.AreEqual(2, c.Capabilities.Scanners.Count, "the compiler exposes the scanners to the flight");
        }

        [Test]
        public void Footprint_GrowsWithAltitude_AndMatchesSmallAngleLimit()
        {
            double R = 600000.0, theta = 20.0 * MathD.DegToRad;
            double prev = 0.0;
            foreach (double h in new[] { 10e3, 50e3, 200e3, 1000e3 })
            {
                double f = ScanGeometry.FootprintRadius(R, h, theta);
                Assert.Greater(f, prev);
                prev = f;
                Assert.LessOrEqual(ScanGeometry.FootprintAngle(R, h, theta), ScanGeometry.HorizonAngle(R, h) + 1e-12);
            }
            double small = ScanGeometry.FootprintRadius(R, 1000.0, theta);
            Assert.AreEqual(1000.0 * Math.Tan(theta), small, 0.01 * small, "h ≪ R: footprint ≈ h·tanθ");
            // Wide cone from far away: limited by the horizon.
            Assert.AreEqual(ScanGeometry.HorizonAngle(R, 5e6), ScanGeometry.FootprintAngle(R, 5e6, 80.0 * MathD.DegToRad), 1e-12);
        }

        [Test]
        public void Scanner_Limits_AltitudeAndResolution()
        {
            ScannerSpec s = Spec(ModuleCatalog.TemperatureRadar);
            double cell = LatLonGrid.Default.CellSize(600000.0);
            Assert.AreEqual(ScannerStatus.TooLow, s.Check(s.MinAltitude * 0.5, cell));
            Assert.AreEqual(ScannerStatus.TooHigh, s.Check(s.MaxAltitude * 1.1, cell));
            Assert.AreEqual(ScannerStatus.Scanning, s.Check(300e3, cell));
            Assert.AreEqual(ScannerStatus.ResolutionTooCoarse, s.Check(2e6, 1000.0));
        }

        [Test]
        public void Coverage_AccumulatesAlongOrbit_AndRotationShiftsTheTrack()
        {
            PlanetScienceModel env = Science(HomeSystemCatalog.HomeWorldId);
            PlanetModel m = env.Model;
            var store = new ScienceMapStore();
            ScienceBodyMap map = store.GetOrCreate("home", "home");
            var scanner = new OrbitalScanner { MeasureBudget = int.MaxValue };
            var scanners = new List<ScannerSpec> { Spec(ModuleCatalog.TemperatureRadar) };
            Circular(m, 300e3, 60.0 * MathD.DegToRad, out Vector3d r0, out Vector3d v0);
            double period = MathD.TwoPi * Math.Sqrt(Math.Pow(m.Radius + 300e3, 3) / m.GravitationalParameter);

            // One big step over half an orbit (time warp): sub-sampling must leave a continuous track.
            ConicPropagator.Propagate(m.GravitationalParameter, r0, v0, 0.5 * period, out Vector3d r1, out Vector3d v1);
            ScanReport rep = scanner.Scan(store, map, env, scanners, r0, v0, 0.0, r1, 0.5 * period);
            Assert.Greater(rep.SubSteps, 10);
            double half = map[ScienceLayer.Temperature].Coverage;
            Assert.Greater(half, 0.0);
            ConicPropagator.Propagate(m.GravitationalParameter, r1, v1, 0.5 * period, out Vector3d r2, out Vector3d v2);
            scanner.Scan(store, map, env, scanners, r1, v1, 0.5 * period, r2, period);
            double one = map[ScienceLayer.Temperature].Coverage;
            Assert.Greater(one, half * 1.5, "coverage accumulates along the orbit");

            // Same ground point after one orbit lands on another longitude: the planet turned underneath.
            Vector3d f0 = m.Rotation.InertialToFixed(r0, 0.0), f1 = m.Rotation.InertialToFixed(r2, period);
            PlanetRotation.LatLonFromDirection(f0, out _, out double lon0);
            PlanetRotation.LatLonFromDirection(f1, out _, out double lon1);
            double expected = -m.Rotation.AngularSpeed * period;
            Assert.AreEqual(MathD.WrapPi(expected), MathD.WrapPi(lon1 - lon0), 1e-3);

            // A second orbit therefore adds new ground.
            ConicPropagator.Propagate(m.GravitationalParameter, r2, v2, period, out Vector3d r3, out _);
            scanner.Scan(store, map, env, scanners, r2, v2, period, r3, 2.0 * period);
            Assert.Greater(map[ScienceLayer.Temperature].Coverage, one * 1.2);
            Assert.AreEqual(0, map.PendingCount);
            // Only the temperature layer was scanned.
            Assert.AreEqual(0, map[ScienceLayer.Pressure].CoveredCount);
        }

        static string AirlessId() => HomeSystemCatalog.Bodies().Find(b => b.Physical.atmosphere == null || !b.Physical.atmosphere.hasAtmosphere).Id;

        static string ThickestAtmosphereId()
        {
            CelestialBodyPreset best = null;
            foreach (var b in HomeSystemCatalog.Bodies())
                if (b.Physical.atmosphere != null && b.Physical.atmosphere.hasAtmosphere &&
                    (best == null || b.Physical.atmosphere.surfacePressure > best.Physical.atmosphere.surfacePressure)) best = b;
            return best.Id;
        }

        [Test]
        public void Airless_Body_PressureIsExactlyZero_ButMeasured()
        {
            PlanetScienceModel env = Science(AirlessId());
            Assert.IsFalse(env.HasAtmosphere);
            var store = new ScienceMapStore();
            ScienceBodyMap map = store.GetOrCreate("home", "home");
            var scanner = new OrbitalScanner { MeasureBudget = int.MaxValue };
            Circular(env.Model, 200e3, 0.3, out Vector3d r, out Vector3d v);
            scanner.Scan(store, map, env, new List<ScannerSpec> { Spec(ModuleCatalog.PressureRadar) }, r, v, 0.0, r, 0.0);
            ScienceLayerMap p = map[ScienceLayer.Pressure];
            Assert.Greater(p.CoveredCount, 0, "vacuum is a measurement, not missing data");
            for (int i = 0; i < p.Grid.CellCount; i++)
                if (p.IsCovered(i)) Assert.AreEqual(0f, p.Value(i));

            PlanetScienceModel quoris = Science(ThickestAtmosphereId());
            SurfaceReading q = quoris.Read(new Vector3d(1, 0, 0));
            Assert.AreEqual(quoris.Model.Atmosphere.Pressure(q.SurfaceHeight), q.Pressure, 1e-6 * q.Pressure, "pressure at the terrain height");
            Assert.Greater(q.Pressure, 1e6);
        }

        [Test]
        public void DifferentBodies_GiveDifferentTemperatures_AndLatitudeMatters()
        {
            // Every body of the system reads differently, ordered like its authored mean temperature.
            var equator = new Vector3d(1, 0, 0);
            var temps = new List<KeyValuePair<double, double>>();
            foreach (CelestialBodyPreset p in HomeSystemCatalog.Bodies())
                temps.Add(new KeyValuePair<double, double>(SurfaceClimate.ReferenceFor(p.Physical.atmosphere, 0.0), Science(p.Id).Read(equator).Temperature));
            temps.Sort((a, b) => a.Key.CompareTo(b.Key));
            KeyValuePair<double, double> coldest = temps[0], hottest = temps[temps.Count - 1];
            Assert.Greater(hottest.Key - coldest.Key, 200.0, "the catalogue spans a wide range");
            Assert.Greater(hottest.Value - coldest.Value, 150.0, "and the instruments see it");

            // Airless body: strong equator-to-pole contrast; dense atmosphere: almost none.
            PlanetScienceModel airless = Science(AirlessId());
            var nearPole = new Vector3d(0.1, 0, 0.995);
            Assert.Greater(airless.Read(equator).Temperature - airless.Read(nearPole).Temperature, 20.0);
            var climate = Science(ThickestAtmosphereId()).Climate;
            double dq = climate.Temperature(0.0, 0.0, climate.MeanAlbedo) - climate.Temperature(1.4, 0.0, climate.MeanAlbedo);
            Assert.Less(dq, 25.0);
            Assert.Greater(dq, 0.0);
        }

        [Test]
        public void Climate_Formulas()
        {
            // Earth-like obliquity: s₂ ≈ −0.477.
            Assert.AreEqual(-0.477, SurfaceClimate.InsolationP2Coefficient(23.44 * MathD.DegToRad), 0.002);
            // Sun-like star at 1 AU, albedo 0.3 → ≈ 255 K.
            Assert.AreEqual(255.0, SurfaceClimate.EquilibriumTemperature(5772.0, 6.957e8, 1.495978707e11, 0.3), 2.0);
            // Authored values win; an unauthored airless body falls back to equilibrium.
            var air = new AtmosphereProperties { hasAtmosphere = false, surfaceTemperature = SurfaceClimate.UnsetTemperature };
            Assert.AreEqual(140.0, SurfaceClimate.ReferenceFor(air, 140.0), 1e-9);
            air.surfaceTemperature = 451.15;
            Assert.AreEqual(451.15, SurfaceClimate.ReferenceFor(air, 140.0), 1e-9);
        }

        [Test]
        public void Footprint_MarksCellsAroundThePoint_IncludingAcrossTheDateLine()
        {
            var layer = new ScienceLayerMap(ScienceLayer.Terrain, LatLonGrid.Default);
            int n = OrbitalScanner.MarkFootprint(layer, 0.0, Math.PI - 0.01, 5.0 * MathD.DegToRad);
            Assert.Greater(n, 10);
            Assert.IsTrue(layer.IsPending(LatLonGrid.Default.CellOf(0.0, -Math.PI + 0.02)), "wraps across ±180°");
            var polar = new ScienceLayerMap(ScienceLayer.Terrain, LatLonGrid.Default);
            OrbitalScanner.MarkFootprint(polar, 0.5 * Math.PI, 0.0, 3.0 * MathD.DegToRad);
            for (int c = 0; c < LatLonGrid.Default.Columns; c++)
                Assert.IsTrue(polar.IsPending(LatLonGrid.Default.Index(LatLonGrid.Default.Rows - 1, c)), "the whole polar cap row");
        }

        [Test]
        public void Archive_RoundTrip_ThroughProfileObjects()
        {
            PlanetScienceModel env = Science(HomeSystemCatalog.TromosId);
            var store = new ScienceMapStore();
            ScienceBodyMap map = store.GetOrCreate(HomeSystemCatalog.TromosId, "Tromos");
            map.Radius = env.Model.Radius;
            var scanner = new OrbitalScanner { MeasureBudget = int.MaxValue };
            Circular(env.Model, 250e3, 1.0, out Vector3d r, out Vector3d v);
            var all = new List<ScannerSpec> { Spec(ModuleCatalog.TemperatureRadar), Spec(ModuleCatalog.PressureRadar), Spec(ModuleCatalog.TerrainScanner) };
            ConicPropagator.Propagate(env.Model.GravitationalParameter, r, v, 3000.0, out Vector3d r1, out _);
            scanner.Scan(store, map, env, all, r, v, 0.0, r1, 3000.0);

            var profile = PlayerProfile.CreateNew("home");
            profile.science = ScienceArchiveCodec.Write(store);
            ScienceMapStore back = ScienceArchiveCodec.Read(profile.science);
            Assert.IsTrue(back.TryGet(HomeSystemCatalog.TromosId, out ScienceBodyMap b));
            Assert.AreEqual(map.Radius, b.Radius);
            foreach (ScienceLayer l in ScienceLayers.All)
            {
                ScienceLayerMap a = map[l], c = b[l];
                Assert.Greater(a.CoveredCount, 0, l.ToString());
                Assert.AreEqual(a.CoveredCount, c.CoveredCount, l.ToString());
                Assert.AreEqual(a.Coverage, c.Coverage, 1e-9);
                for (int i = 0; i < a.Grid.CellCount; i++)
                {
                    Assert.AreEqual(a.IsCovered(i), c.IsCovered(i));
                    if (!a.IsCovered(i)) continue;
                    for (int ch = 0; ch < a.ChannelCount; ch++)
                    {
                        double x = a.Value(i, ch), y = c.Value(i, ch);
                        double tol = l == ScienceLayer.Pressure ? 1e-3 * Math.Abs(x) : l == ScienceLayer.Temperature ? 0.06 : ch == 0 ? 1.01 : 1e-4;
                        Assert.AreEqual(x, y, tol, l + " ch" + ch);
                    }
                }
            }
            // Old saves have no archive (null after JSON load) or a broken one: an empty store, never an exception.
            Assert.AreEqual(0, ScienceArchiveCodec.Read(null).Bodies.Count);
            var broken = new ScienceArchive();
            broken.bodies.Add(new ScienceBodyRecord { bodyId = "x", layers = { new ScienceLayerRecord { layer = 0, coverage = "!!", values = "??" } } });
            Assert.AreEqual(0, ScienceArchiveCodec.Read(broken).Bodies[0][ScienceLayer.Temperature].CoveredCount);
        }

        [Test]
        public void InSitu_MeasuresTheCellUnderTheVehicle()
        {
            PlanetScienceModel env = Science(HomeSystemCatalog.TromosId);
            var store = new ScienceMapStore();
            ScienceBodyMap map = store.GetOrCreate("t", "t");
            var dir = PlanetRotation.DirectionFromLatLon(0.3, -1.0);
            Assert.IsTrue(new OrbitalScanner().MeasureInSitu(store, map, env, ScienceLayer.Temperature, dir * env.Model.Radius));
            Assert.AreEqual(1, map[ScienceLayer.Temperature].CoveredCount);
            Assert.IsTrue(map[ScienceLayer.Temperature].IsCovered(map.Grid.CellOf(0.3, -1.0)));
        }
    }
}
