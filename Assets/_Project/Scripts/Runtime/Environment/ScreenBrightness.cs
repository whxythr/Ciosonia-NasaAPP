using UnityEngine;
#if SPACESIM_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Applies the player's brightness (<see cref="GameSettings.Brightness"/>) to the flight camera as a post-exposure in EV
    /// (a global URP Volume with Color Adjustments, on top of the look from <see cref="GraphicsPostProcessing"/>). At 100 % this volume is
    /// off and the image is the look alone. Visual only — light levels in the simulation are unchanged.
    /// </summary>
    public sealed class ScreenBrightness : MonoBehaviour
    {
#if SPACESIM_URP
        Volume volume;
        VolumeProfile profile;
        ColorAdjustments adjustments;
#endif

        /// <summary>Exposure offset currently applied [EV].</summary>
        public float AppliedExposure { get; private set; }

        /// <summary>True while a non-default brightness is being applied.</summary>
        public bool PostProcessingActive { get; private set; }

        public void Initialize(Camera camera)
        {
            Build();
            Apply();
        }

        void OnEnable() => GameSettings.Changed += Apply;
        void OnDisable() => GameSettings.Changed -= Apply;

        void OnDestroy()
        {
#if SPACESIM_URP
            if (profile != null) Destroy(profile);
#endif
        }

        void Build()
        {
#if SPACESIM_URP
            if (volume != null) return;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Brightness (runtime)";
            profile.hideFlags = HideFlags.DontSave;
            adjustments = profile.Add<ColorAdjustments>(false); // only post-exposure is overridden
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = profile;
#endif
        }

        void Apply()
        {
            float ev = GameSettings.ExposureEv;
            bool active = Mathf.Abs(ev) > 0.005f;
            AppliedExposure = active ? ev : 0f;
            PostProcessingActive = active;
#if SPACESIM_URP
            if (adjustments != null) adjustments.postExposure.Override(AppliedExposure);
            if (volume != null) volume.enabled = active;
#endif
        }
    }
}
