using System.Collections;
using NUnit.Framework;
using SpaceSim.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceSim.Tests
{
    /// <summary>The 3D map replaces the flight view while open, zooms with the wheel's logic and centres on what is chosen.</summary>
    public class OrbitalMapPlayModeTests
    {
        GameObject root;
        string saveDir;

        [SetUp]
        public void SetUp()
        {
            // Never read or write the player's real profile from tests.
            saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpaceSimMapTest_" + System.Guid.NewGuid().ToString("N"));
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

        [UnityTest]
        public IEnumerator Map_OpensOverFlightView_Zooms_Focuses_AndCloses()
        {
            root = new GameObject("SpaceSimMapTest");
            var boot = root.AddComponent<SpaceSimBootstrap>();
            for (int i = 0; i < 20; i++) yield return null;

            OrbitalMapView map = boot.Map;
            Assert.IsFalse(map.IsOpen);
            Assert.IsNull(boot.Navigation.CurrentOrbit(), "On the pad there is no orbit to show.");

            map.Open();
            yield return null;
            Assert.IsTrue(map.IsOpen);
            Assert.IsTrue(FlightInput.MapOpen);
            Assert.IsFalse(boot.CameraController.Camera.enabled, "The flight camera is off while the map is drawn.");
            Assert.AreEqual(boot.System.ActiveIndex, map.Focus);
            Assert.IsTrue(boot.Hud.AnyWindowOpen, "Esc closes the map first.");

            double start = map.ViewDistance;
            map.Zoom(3f);
            Assert.Less(map.ViewDistance, start);
            for (int i = 0; i < 400; i++) map.Zoom(1f);
            Assert.AreEqual(boot.System.ActiveBody.Runtime.Model.Radius * 1.15, map.ViewDistance, 1.0, "Zooming in stops above the surface.");

            map.SetFocus(-1);
            yield return null;
            Assert.AreEqual(-1, map.Focus);

            Assert.IsTrue(boot.Hud.CloseTopWindow());
            Assert.IsFalse(map.IsOpen);
            Assert.IsFalse(FlightInput.MapOpen);
            Assert.IsTrue(boot.CameraController.Camera.enabled);
        }
    }
}
