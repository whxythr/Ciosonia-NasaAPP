using System.Collections;
using NUnit.Framework;
using SpaceSim.Audio;
using SpaceSim.Economy;
using SpaceSim.Navigation;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Runtime;
using SpaceSim.Science;
using SpaceSim.Vehicles;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Cross-feature integration in the running game: the soundtrack follows menu в†’ flight, orbital scanning collects and saves
    /// maps on a propagated orbit (and in a Career test-tool coverage is written off unpaid, the instant manoeuvre is hidden),
    /// and flight planning picks a trajectory point, warps to it, places a node and updates the prediction.
    /// </summary>
    public class IntegrationPlayModeTests
    {
        GameObject root;
        string saveDir;

        [SetUp]
        public void SetUp()
        {
            saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpaceSimIntegrationTest_" + System.Guid.NewGuid().ToString("N"));
            PlayerSession.SaveDirectoryOverride = saveDir;
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.Destroy(root);
            var cam = GameObject.Find("SpaceCamera");
            if (cam != null) Object.Destroy(cam);
            PlayerSession.SaveDirectoryOverride = null;
            if (System.IO.Directory.Exists(saveDir)) System.IO.Directory.Delete(saveDir, true);
        }

        IEnumerator Boot(System.Action<SpaceSimBootstrap> done)
        {
            root = new GameObject("SpaceSimIntegrationTest");
            var boot = root.AddComponent<SpaceSimBootstrap>();
            for (int i = 0; i < 20; i++) yield return null;
            done(boot);
        }

        /// <summary>Circular prograde orbit over the equator at <paramref name="altitude"/> (test tool: inertial ENU velocity).</summary>
        static void PutInOrbit(SpaceSimBootstrap boot, double altitude)
        {
            double mu = boot.Planet.Model.GravitationalParameter, r = boot.Planet.Model.Radius + altitude;
            boot.Body.ApplyInitialConditions(0.0, 0.0, altitude, System.Math.Sqrt(mu / r), 0.0, 0.0, false);
        }

        static IEnumerator RunSimulated(SpaceSimBootstrap boot, double seconds, double warp, float realLimit)
        {
            double start = boot.Simulation.SimulationTime;
            float deadline = Time.realtimeSinceStartup + realLimit;
            while (boot.Simulation.SimulationTime - start < seconds && Time.realtimeSinceStartup < deadline)
            {
                boot.Simulation.SetWarp(warp);
                yield return null;
            }
            boot.Simulation.ResetWarp();
        }

        static RocketBlueprint ScannerProbe()
        {
            RocketBlueprint b = RocketBlueprint.CreateDefault();
            b.payloadModules.Add(ModuleCatalog.TerrainScanner);
            b.payloadModules.Add(ModuleCatalog.TemperatureRadar);
            b.payloadModules.Add(ModuleCatalog.BatteryLarge);
            return b;
        }

        [UnityTest]
        public IEnumerator Music_MenuTrackThenFlightZone()
        {
            MusicDirector director = MusicDirector.Ensure();
            Assert.IsNotNull(director);
            director.EnterMenu();
            float until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(director.InMenu);
            Assert.AreEqual(MusicLibrary.MenuTrack, director.Selection.TrackId, "main menu plays the menu track");

            SpaceSimBootstrap boot = null;
            yield return Boot(b => boot = b);
            until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.AreSame(director, MusicDirector.Instance, "one persistent player across scenes");
            Assert.IsFalse(director.InMenu, "the flight scene took over the soundtrack");
            Assert.AreNotEqual(MusicSelector.MenuZone, director.Selection.ZoneKey, "selection follows the vehicle, not the menu");
        }

        [UnityTest]
        public IEnumerator Sandbox_ScannerMapsAPropagatedOrbit_AndSavesTheMap()
        {
            SpaceSimBootstrap boot = null;
            yield return Boot(b => boot = b);
            Assert.IsTrue(boot.Session.Profile.IsSandbox);
            Assert.IsTrue(boot.Navigation.Maneuvers.InstantBurnAllowed, "sandbox keeps the instant manoeuvre");
            Assert.IsTrue(boot.Body.ApplyBlueprint(ScannerProbe()));
            yield return null;
            Assert.AreEqual(2, boot.Body.Capabilities.Scanners.Count);

            PutInOrbit(boot, 400e3);
            yield return RunSimulated(boot, 900.0, 1000.0, 25f);
            Assert.IsTrue(boot.Science.Store.TryGet(HomeSystemCatalog.HomeWorldId, out ScienceBodyMap map), "a map of the home planet");
            Assert.Greater(map[ScienceLayer.Terrain].CoveredCount, 0, "terrain cells measured along the ground track");
            Assert.Greater(map[ScienceLayer.Temperature].CoveredCount, 0);

            int credits = boot.Session.Profile.credits;
            boot.Science.Flush();
            PlayerProfile saved = PlayerSession.ReadProfileFile();
            Assert.IsNotNull(saved);
            ScienceMapStore reloaded = ScienceArchiveCodec.Read(saved.science);
            Assert.IsTrue(reloaded.TryGet(HomeSystemCatalog.HomeWorldId, out ScienceBodyMap back));
            Assert.AreEqual(map[ScienceLayer.Terrain].CoveredCount, back[ScienceLayer.Terrain].CoveredCount, "the saved map is the map");
            Assert.AreEqual(credits, saved.credits, "sandbox earns nothing from mapping");
            Assert.AreEqual(0, saved.scienceRewards.Count);
        }

        [UnityTest]
        public IEnumerator Career_TestToolCoverageIsNotPaid_AndInstantBurnHidden()
        {
            NewGame.Start(GameMode.Career);
            SpaceSimBootstrap boot = null;
            yield return Boot(b => boot = b);
            PlayerProfile p = boot.Session.Profile;
            Assert.IsTrue(p.IsCareer);
            Assert.IsFalse(boot.Navigation.Maneuvers.InstantBurnAllowed, "career pays for its О”v");
            foreach (string id in new[] { ModuleCatalog.TerrainScanner, ModuleCatalog.TemperatureRadar, ModuleCatalog.BatteryLarge })
                p.ownedModules.Add(id);
            Assert.IsTrue(boot.Body.ApplyBlueprint(ScannerProbe()));
            yield return null;
            Assert.AreEqual(2, boot.Body.Capabilities.Scanners.Count, "owned scanners fly in a career");

            int credits = p.credits;
            double research = p.researchPoints;
            PutInOrbit(boot, 400e3); // a test tool: rewards stop counting for this flight
            Assert.IsTrue(boot.Body.UsedTestTool);
            yield return RunSimulated(boot, 12000.0, 1000.0, 40f); // ~2 orbits: well over 1 % of the surface
            boot.Science.Flush();
            Assert.IsTrue(boot.Science.Store.TryGet(HomeSystemCatalog.HomeWorldId, out ScienceBodyMap map));
            Assert.Greater(ScienceRewards.PercentOf(map[ScienceLayer.Temperature].Coverage), 0, "at least one whole percent mapped");
            int written = 0;
            foreach (ScienceRewardRecord r in p.scienceRewards) written += r.rewardedPercent;
            Assert.Greater(written, 0, "coverage was settled (written off)");
            Assert.IsFalse(boot.Science.LastPayout.Paid);
            Assert.AreEqual(credits, p.credits);
            Assert.AreEqual(research, p.researchPoints);
            PlayerProfile saved = PlayerSession.ReadProfileFile();
            Assert.Greater(saved.scienceRewards.Count, 0, "the written-off share is saved, so it is never paid later");
            Assert.AreEqual(GameMode.Career, saved.gameMode);
        }

        [UnityTest]
        public IEnumerator Planning_PickPoint_WarpToIt_PlaceNode_PredictionUpdates()
        {
            SpaceSimBootstrap boot = null;
            yield return Boot(b => boot = b);
            NavigationService nav = boot.Navigation;
            ManeuverController m = nav.Maneuvers;
            PutInOrbit(boot, 400e3);
            for (int i = 0; i < 5; i++) yield return null;
            nav.RefreshNow();
            Assert.IsNotNull(nav.Prediction, "an orbit has a prediction");

            // Pick a point a quarter of the way along the first patch and warp there.
            TrajectoryPoint point = PickAhead(boot, nav);
            Assert.IsTrue(point.Valid);
            Assert.Greater(point.Time, boot.Simulation.SimulationTime + 60.0);
            m.Select(point);
            m.WarpToSelected();
            Assert.IsTrue(m.Warping);
            float deadline = Time.realtimeSinceStartup + 40f;
            while (m.Warping && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(m.Warping, "arrived");
            Assert.AreEqual(point.Time, boot.Simulation.SimulationTime, 2.0, "time warp stopped at the point");
            Assert.AreEqual(1.0, boot.Simulation.TimeWarp, 1e-9, "back to real time");

            // A node ahead with a prograde burn: the predicted orbit after it is higher.
            nav.RefreshNow();
            m.Select(PickAhead(boot, nav));
            m.CreateNodeAtSelected();
            Assert.IsNotNull(m.Node);
            m.SetComponents(150.0, 0.0, 0.0);
            float until = Time.realtimeSinceStartup + 1.0f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.IsNotNull(nav.Prediction);
            Assert.IsTrue(nav.Prediction.NodeApplied, "the prediction includes the node");
            Assert.IsTrue(m.Preview.Valid);
            Assert.Greater(m.Preview.After.ApoapsisRadius, m.Preview.Before.ApoapsisRadius + 100e3, "+150 m/s prograde raises the apoapsis");
            m.DeleteNode();
            Assert.IsNull(m.Node);
        }

        static TrajectoryPoint PickAhead(SpaceSimBootstrap boot, NavigationService nav)
        {
            TrajectoryPrediction p = nav.Prediction;
            CoastState now = boot.Body.Simulation.CoastStateNow();
            int samples = p.Patches[0].Times.Count;
            return TrajectoryPoints.PointOnPatch(nav.System, p, 0, samples / 4, 0.5, now);
        }
    }
}
