using UnityEngine;
#if SPACESIM_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The picture's final look: a global URP Volume on the flight camera with filmic tone mapping (the sky, the Sun, flames and snow all
    /// arrive in scene-linear HDR), bloom for what is brighter than white, and a light grade. Created from code, no asset to lose.
    /// The player's brightness is a separate, higher-priority volume (<see cref="ScreenBrightness"/>) that adds post-exposure on top of this.
    /// </summary>
    public sealed class GraphicsPostProcessing : MonoBehaviour
    {
#pragma warning disable CS0414 // read only in the SPACESIM_URP branch below
        [Tooltip("Scene brightness at which bloom starts. Keep above sunlit snow and clouds (about 1.4), so only the Sun, flames and sun glitter bloom.")]
        [SerializeField] float bloomThreshold = 1.7f;
        [SerializeField] float bloomIntensity = 0.55f;
        [Range(0f, 1f)] [SerializeField] float bloomScatter = 0.68f;
        [Range(-100f, 100f)] [SerializeField] float contrast = 5f;
        [Range(-100f, 100f)] [SerializeField] float saturation = 0f;
#pragma warning restore CS0414

#if SPACESIM_URP
        Volume volume;
        VolumeProfile profile;
#endif

        /// <summary>Retunes the look at run time (bridge "gfx"). Negative = keep.</summary>
        public void Configure(float threshold, float intensity, float contrastValue, float saturationValue)
        {
            if (threshold >= 0f) bloomThreshold = threshold;
            if (intensity >= 0f) bloomIntensity = intensity;
            if (contrastValue > -100f) contrast = contrastValue;
            if (saturationValue > -100f) saturation = saturationValue;
#if SPACESIM_URP
            if (profile == null) return;
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.threshold.Override(bloomThreshold);
                bloom.intensity.Override(bloomIntensity);
            }
            if (profile.TryGet(out ColorAdjustments grade))
            {
                grade.contrast.Override(contrast);
                grade.saturation.Override(saturation);
            }
#endif
        }

        public string Describe() => "bloom thr=" + bloomThreshold + " int=" + bloomIntensity + " contrast=" + contrast + " saturation=" + saturation;

        public void Initialize(Camera camera)
        {
#if SPACESIM_URP
            if (volume == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "SpaceSim look (runtime)";
                profile.hideFlags = HideFlags.DontSave;

                Tonemapping tonemapping = profile.Add<Tonemapping>(false);
                tonemapping.mode.Override(TonemappingMode.ACES);

                Bloom bloom = profile.Add<Bloom>(false);
                bloom.threshold.Override(bloomThreshold);
                bloom.intensity.Override(bloomIntensity);
                bloom.scatter.Override(bloomScatter);
                bloom.highQualityFiltering.Override(false);
                bloom.maxIterations.Override(6);

                // No post-exposure here: that parameter belongs to ScreenBrightness.
                ColorAdjustments grade = profile.Add<ColorAdjustments>(false);
                grade.contrast.Override(contrast);
                grade.saturation.Override(saturation);

                volume = gameObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10f;
                volume.sharedProfile = profile;
            }
            var data = camera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = true;
                data.dithering = true; // sky gradients in 8-bit output
            }
#endif
        }

        void OnDestroy()
        {
#if SPACESIM_URP
            if (profile != null) Destroy(profile);
#endif
        }
    }
}
