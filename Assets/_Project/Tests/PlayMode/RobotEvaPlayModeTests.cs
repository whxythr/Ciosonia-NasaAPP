using System.Collections;
using NUnit.Framework;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using SpaceSim.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceSim.Tests
{
    /// <summary>
    /// The surface robot in the running game: the status panel and the visor overlay follow the <see cref="RobotStatus"/>,
    /// 0 % visor blacks the view out, a wrecked robot is pulled back in; the imported-model adapter fits, strips and converts
    /// a stand-in "prefab" (the Asset Store model itself is not part of the repository), and without one the procedural
    /// astronaut stays.
    /// </summary>
    public class RobotEvaPlayModeTests
    {
        GameObject root, template;
        RobotModelSettings settings;
        string saveDir;

        [SetUp]
        public void SetUp()
        {
            saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpaceSimRobotTest_" + System.Guid.NewGuid().ToString("N"));
            PlayerSession.SaveDirectoryOverride = saveDir;
        }

        [TearDown]
        public void TearDown()
        {
            RobotModelSettings.Override = null;
            if (root != null) Object.Destroy(root);
            if (template != null) Object.Destroy(template);
            if (settings != null) Object.Destroy(settings);
            var cam = GameObject.Find("SpaceCamera");
            if (cam != null) Object.Destroy(cam);
            PlayerSession.SaveDirectoryOverride = null;
            if (System.IO.Directory.Exists(saveDir)) System.IO.Directory.Delete(saveDir, true);
        }

        /// <summary>A two-part "robot" with built-in Standard materials, a collider, a rigidbody and a stray light.</summary>
        GameObject StandIn()
        {
            template = new GameObject("RobotSphereStandIn");
            template.SetActive(false);
            Shader standard = Shader.Find("Standard");
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.SetParent(template.transform, false);
            ball.transform.localPosition = new Vector3(0f, 1f, 0f);
            ball.transform.localScale = Vector3.one * 2f; // 2 units tall body
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(template.transform, false);
            head.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            head.transform.localScale = Vector3.one * 0.8f; // top at 2.7 units
            if (standard != null)
                foreach (Renderer r in template.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = new Material(standard);
            template.AddComponent<Rigidbody>();
            return template;
        }

        [Test]
        public void RobotAdapterFitsStripsAndConvertsAStandIn()
        {
            settings = ScriptableObject.CreateInstance<RobotModelSettings>();
            settings.prefab = StandIn();
            settings.height = 1.1f;
            settings.firstPersonHideRadius = 0.05f;
            settings.rollingPart = "Ball";
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Assert.IsNotNull(lit);
            var robot = new RobotBodyModel(settings, lit);
            try
            {
                Assert.AreEqual(0, robot.Root.GetComponentsInChildren<Collider>(true).Length, "vendor physics removed");
                Assert.AreEqual(0, robot.Root.GetComponentsInChildren<Rigidbody>(true).Length);
                var bounds = new Bounds();
                bool first = true;
                foreach (Renderer r in robot.Root.GetComponentsInChildren<Renderer>())
                {
                    if (first) bounds = r.bounds;
                    else bounds.Encapsulate(r.bounds);
                    first = false;
                    foreach (Material m in r.sharedMaterials) Assert.AreEqual(lit, m.shader, r.name + ": built-in material converted for URP");
                }
                Assert.AreEqual(1.1f, bounds.size.y, 0.02f, "fitted to the height");
                Assert.AreEqual(0f, bounds.min.y, 0.02f, "feet on the ground");
                Assert.Greater(robot.HeadRenderers.Count, 0);
                Assert.IsTrue(robot.FirstPerson);
                foreach (Renderer r in robot.HeadRenderers) Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, r.shadowCastingMode);
                foreach (Renderer r in robot.BodyRenderers) Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, r.shadowCastingMode);
                robot.SetFirstPerson(false);
                foreach (Renderer r in robot.HeadRenderers) Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, r.shadowCastingMode);

                // Walking leans the body into the acceleration and the eye stays at the head.
                var start = new Vector3d(1, 0, 0);
                var walker = new SurfaceWalker(3.986e14, 6.371e6, null, start);
                walker.PlaceAt(start * 6.371e6);
                float maxLean = 0f;
                for (int i = 0; i < 60; i++)
                {
                    walker.Step(0.02, 1.0, 0.0, 0.0, true, false);
                    robot.Animate(walker, new Vector3d(0, 0, 1), new Vector3d(0, -1, 0), 0.02f);
                    maxLean = Mathf.Max(maxLean, robot.LeanDegrees);
                }
                Assert.Greater(maxLean, 3f, "tan θ = a/g while speeding up");
                Assert.AreEqual(1.1f * settings.eyeHeightFraction, robot.EyeLocal.y, 0.2f);
            }
            finally
            {
                robot.Destroy();
            }
        }

        [UnityTest]
        public IEnumerator Eva_StatusPanel_VisorBlackout_Disabled()
        {
            root = new GameObject("SpaceSimTest");
            var boot = root.AddComponent<SpaceSimBootstrap>();
            for (int i = 0; i < 20; i++) yield return null;

            var ex = boot.Explorer;
            Assert.AreEqual("", ex.TryStartEva());
            Assert.IsTrue(ex.Eva);
            Assert.IsInstanceOf<AstronautModel>(ex.Model, "no robot set up in the test project: the procedural fallback");
            yield return null;
            yield return null;
            Assert.IsTrue(ex.StatusPanel.Visible, "the status panel shows outside");
            Assert.AreSame(ex.Robot, ex.StatusPanel.Status);
            Assert.AreEqual(1.0, ex.Robot.Visor);
            Assert.IsFalse(ex.VisorOverlay.Blackout);

            // A half-broken visor: noise and darkening, not black yet.
            ex.Robot.Set(RobotCondition.Visor, 0.4);
            yield return null;
            Assert.AreEqual(0.4f, ex.VisorOverlay.Visor, 1e-4f);
            Assert.IsFalse(ex.VisorOverlay.Blackout);
            Assert.Greater(RobotVisorOverlay.Darkening(0.4f), 0f);
            Assert.Less(RobotVisorOverlay.Darkening(0.4f), 1f);
            Assert.Greater(RobotVisorOverlay.CrackCount(0.4f), 0);
            Assert.Greater(RobotVisorOverlay.NoiseAlpha(0.4f), 0f);

            // 0 %: only the status panel remains.
            ex.Robot.Set(RobotCondition.Visor, 0.0);
            yield return null;
            Assert.IsTrue(ex.VisorOverlay.Blackout);
            Assert.AreEqual(1f, RobotVisorOverlay.Darkening(0f));
            Assert.Less(RobotVisorOverlay.StatusDepth, RobotVisorOverlay.BlackoutDepth, "the status is drawn in front of the blackout");
            Assert.IsTrue(ex.StatusPanel.Visible);
            Assert.IsTrue(ex.Eva, "a blind robot keeps working");

            // A wrecked hull: the tether pulls the robot in, and it cannot go out again.
            ex.Robot.Damage(RobotCondition.Integrity, 1.0);
            yield return null;
            Assert.IsFalse(ex.Eva);
            Assert.IsNull(ex.Model);
            Assert.IsFalse(ex.StatusPanel.Visible);
            Assert.IsFalse(ex.VisorOverlay.Blackout);
            Assert.AreNotEqual("", ex.TryStartEva());

            // A new rocket brings a fresh robot.
            boot.NewRocketAtHome();
            yield return null;
            Assert.IsFalse(ex.Robot.Disabled);
            Assert.AreEqual(1.0, ex.Robot.Visor);
            Assert.AreEqual("", ex.TryStartEva());
            ex.EndEva();
        }

        [UnityTest]
        public IEnumerator Eva_UsesTheRobotModelWhenSetUp()
        {
            settings = ScriptableObject.CreateInstance<RobotModelSettings>();
            settings.prefab = StandIn();
            RobotModelSettings.Override = settings;
            root = new GameObject("SpaceSimTest");
            var boot = root.AddComponent<SpaceSimBootstrap>();
            for (int i = 0; i < 20; i++) yield return null;

            var ex = boot.Explorer;
            Assert.AreEqual("", ex.TryStartEva());
            Assert.IsInstanceOf<RobotBodyModel>(ex.Model);
            ex.Drive(1.0, 0.0, 1.0f, false);
            float end = Time.realtimeSinceStartup + 1.2f; // one real second of walking, whatever the frame rate
            while (Time.realtimeSinceStartup < end) yield return null;
            Assert.Greater(ex.Walker.DistanceWalked, 0.3, "walked ≈ 1.36 m/s × 1 s minus the start-up");
            Assert.Less((ex.Model.Root.position - boot.CameraController.transform.position).magnitude, 2f, "the robot stands under the camera");
            ex.EndEva();
            Assert.IsNull(ex.Model);
        }
    }
}
