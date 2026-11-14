using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Which imported model plays the surface robot and how it is fitted (Resources/<see cref="ResourcePath"/>). The asset
    /// is created and filled by the editor tool SpaceSim ▸ Robot ▸ Set up Robot Sphere model (also run automatically after
    /// the Asset Store package "Robot Sphere" is imported). Without the asset, or with no prefab in it, the EVA falls back
    /// to the procedural <see cref="AstronautModel"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "RobotModelSettings", menuName = "SpaceSim/Robot Model Settings")]
    public sealed class RobotModelSettings : ScriptableObject
    {
        public const string ResourcePath = "Robot/RobotModelSettings";

        [Tooltip("Prefab or model of the robot (e.g. the Asset Store package \"Robot Sphere\").")]
        public GameObject prefab;

        [Header("Fit [m]")]
        [Tooltip("The model is scaled uniformly to this height.")]
        public float height = 1.1f;
        [Tooltip("Eye (camera) height as a share of the height.")]
        [Range(0.1f, 1.2f)] public float eyeHeightFraction = 0.82f;
        [Tooltip("Eye in front of the model's centre [m].")]
        public float eyeForward = 0.12f;
        [Tooltip("Turns the model so its face looks along +Z [degrees].")]
        public float yawOffsetDegrees;
        [Tooltip("In first person, renderers whose bounds come this close to the eye only cast shadows [m].")]
        public float firstPersonHideRadius = 0.3f;

        [Header("Animation (names that do not exist in the controller are ignored)")]
        public string speedParameter = "Speed";
        public string groundedParameter = "Grounded";
        public string idleState = "Idle";
        public string walkState = "Walk";
        public string runState = "Run";
        public string airState = "Jump";
        [Tooltip("Walking speed [m/s] at which the walk clip plays at its own pace.")]
        public float walkClipSpeed = 1.3f;

        [Header("Rolling body (optional)")]
        [Tooltip("Child transform that rolls with the distance covered (a ball body); empty = none.")]
        public string rollingPart = "";
        [Tooltip("Radius of the rolling part at the fitted scale [m]; 0 = from its bounds.")]
        public float rollingRadius;

        static RobotModelSettings cached;
        static bool loaded;

        /// <summary>Used instead of the Resources asset when set (tests, the development bridge).</summary>
        public static RobotModelSettings Override { get; set; }

        /// <summary>The project's settings, or null when the robot has not been set up.</summary>
        public static RobotModelSettings Load()
        {
            if (Override != null) return Override;
            if (!loaded || cached == null)
            {
                cached = Resources.Load<RobotModelSettings>(ResourcePath);
                loaded = true;
            }
            return cached;
        }

        /// <summary>Forgets the cached asset (after the editor tool changed it).</summary>
        public static void ClearCache()
        {
            cached = null;
            loaded = false;
        }

        public bool Usable => prefab != null && height > 0.05f;
    }
}
