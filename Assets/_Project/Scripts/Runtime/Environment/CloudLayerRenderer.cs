using System;
using System.Threading.Tasks;
using SpaceSim.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// One planet's cloud deck: decides whether it exists (only with air thick enough to hold clouds), computes its altitude and look from the
    /// planet's data, keeps the weather map, and draws the ray-marched shell. Noise coordinates are camera-relative and wrapped in double
    /// precision, so the pattern is stable at any distance from the origin. Purely visual: nothing here feeds back into physics.
    /// </summary>
    [DefaultExecutionOrder(125)]
    public sealed class CloudLayerRenderer : MonoBehaviour
    {
        /// <summary>Period of the wrapped noise coordinates [m]; every noise tile in the shaders divides it.</summary>
        public const double NoiseWrap = 96000.0;

        static readonly int CloudColorId = Shader.PropertyToID("_CloudColor");
        static readonly int ExtinctionId = Shader.PropertyToID("_CloudExtinction");
        static readonly int StepsId = Shader.PropertyToID("_CloudSteps");
        static readonly int LightStepsId = Shader.PropertyToID("_CloudLightSteps");
        static readonly int AmbientId = Shader.PropertyToID("_CloudAmbient");
        static readonly int FadeId = Shader.PropertyToID("_CloudFade");
        static readonly int MapTexelId = Shader.PropertyToID("_CloudMapTexel");
        static readonly int CirrusId = Shader.PropertyToID("_CloudCirrus");
        static readonly int CirrusHeightId = Shader.PropertyToID("_CloudCirrusHeight");
        static readonly int CloudMapId = Shader.PropertyToID("_SpaceSimCloudMap");

        PlanetRuntime planet;
        FloatingOrigin origin;
        StarSystemRuntime system;
        Material material;
        MeshRenderer meshRenderer;
        Texture2D weatherTexture;
        CloudWeatherMap weather;
        Task<CloudWeatherMap> job;
        int jobSeed = int.MinValue;
        double jobCoverage = -1.0;
        int jobWidth;
        float fade;

        public float BaseAltitude { get; private set; } = 1500f;
        public float TopAltitude { get; private set; } = 9000f;
        /// <summary>0 while the deck is absent or the weather is not ready.</summary>
        public float ShadowStrength { get; private set; }
        public Vector3 NoiseOrigin { get; private set; }
        public Vector4 Row0 { get; private set; } = new Vector4(1f, 0f, 0f, 0f);
        public Vector4 Row1 { get; private set; } = new Vector4(0f, 1f, 0f, 0f);
        public Vector4 Row2 { get; private set; } = new Vector4(0f, 0f, 1f, 0f);
        public Texture WeatherTexture => weatherTexture;
        /// <summary>The material of the deck (null if the shader is missing).</summary>
        public Material Material => material;
        /// <summary>Fraction of direct sunlight reaching the viewer through the deck (1 = clear). Dims the Unity light on rockets and buildings.</summary>
        public float SunlightAtViewer { get; private set; } = 1f;

        /// <summary>Diagnostics (bridge): state of the deck.</summary>
        public string Describe() =>
            "weather=" + (weatherTexture != null ? weatherTexture.width + "x" + weatherTexture.height : job != null ? "generating" : "none") +
            " noise=" + SpaceSimNoiseTextures.Ready + " coverage=" + jobCoverage.ToString("0.00") + " fade=" + fade.ToString("0.00") +
            " visible=" + (meshRenderer != null && meshRenderer.enabled) + " base=" + BaseAltitude.ToString("0") + "m top=" + TopAltitude.ToString("0") +
            "m shadow=" + ShadowStrength.ToString("0.00") + " sunAtViewer=" + SunlightAtViewer.ToString("0.00");

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin floatingOrigin, StarSystemRuntime starSystem, Mesh shellMesh)
        {
            planet = planetRuntime;
            origin = floatingOrigin;
            system = starSystem;
            material = ProceduralMeshes.EffectMaterial("SpaceSim/Clouds");
            if (material == null)
            {
                Debug.LogError("[SpaceSim] Shader SpaceSim/Clouds not found.", this);
                return;
            }
            material.name = "Clouds · " + planet.Definition.displayName;
            gameObject.AddComponent<MeshFilter>().sharedMesh = shellMesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = false;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (weatherTexture != null) Destroy(weatherTexture);
        }

        /// <summary>Cloud fraction this planet actually shows: the stored parameter, faded out for air too thin to hold clouds.</summary>
        public static double VisibleCoverage(PlanetDefinition def, double surfacePressure) =>
            MathD.Clamp01(def.generation.cloudCoverage) * (1.0 - Math.Exp(-surfacePressure / 3000.0));

        /// <summary>
        /// Called by <see cref="AtmosphereRenderer"/> before it sets the shared uniforms. Runs even with the deck hidden, so a planet is ready when
        /// the camera turns to it.
        /// </summary>
        public void Tick(double renderTime, bool hasAir, Vector3 sunDirUnity, double shellHeight, double scaleHeight, Vector3d cameraRelative)
        {
            if (material == null || planet.Model == null) return;
            var def = planet.Definition;
            var settings = def.atmosphereRendering.clouds;
            var model = planet.Model;
            double pressure = model.Atmosphere.Pressure(0.0);
            double coverage = hasAir && settings.enabled ? VisibleCoverage(def, pressure) : 0.0;
            bool wanted = coverage > 0.02;

            if (wanted) UpdateWeather(def.generation.seed, coverage, settings);
            ComputeFrame(renderTime, settings, def, pressure, scaleHeight, sunDirUnity, cameraRelative);

            bool ready = wanted && weatherTexture != null && SpaceSimNoiseTextures.Ready;
            fade = ready ? Mathf.Min(1f, fade + Time.unscaledDeltaTime / 2f) : 0f;
            ShadowStrength = ready ? settings.shadowStrength * fade : 0f;
            if (!ready)
            {
                SunlightAtViewer = 1f;
                meshRenderer.enabled = false;
                return;
            }

            // Viewer on the ground under the deck: where does the Sun ray cross it?
            UpdateSunlightAtViewer(sunDirUnity, cameraRelative, model.Radius);

            transform.SetPositionAndRotation(planet.UnityCenter(origin), Quaternion.identity);
            transform.localScale = Vector3.one * (float)((model.Radius + shellHeight) * 1.03 * planet.RenderScale);

            // Distant bodies need only a coarse march.
            Camera cam = Camera.main;
            float pixels = 400f;
            if (cam != null)
            {
                double dist = Math.Max(cameraRelative.Magnitude, model.Radius);
                pixels = (float)(model.Radius / dist) * Screen.height / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            }
            meshRenderer.enabled = pixels > 2f;

            Color tint = new Color(def.generation.cloudTint.r, def.generation.cloudTint.g, def.generation.cloudTint.b, 1f).linear;
            material.SetColor(CloudColorId, tint);
            material.SetFloat(ExtinctionId, settings.extinction);
            material.SetFloat(StepsId, Mathf.Round(Mathf.Lerp(6f, settings.primarySteps, Mathf.InverseLerp(40f, 500f, pixels))));
            material.SetFloat(LightStepsId, settings.lightSteps);
            material.SetFloat(AmbientId, settings.ambient);
            material.SetFloat(FadeId, fade);
            material.SetFloat(MapTexelId, (float)(2.0 * Math.PI * model.Radius / Math.Max(1, weather.Width)));
            material.SetFloat(CirrusId, settings.cirrus);
            material.SetFloat(CirrusHeightId, TopAltitude + (float)(scaleHeight * 0.4));
            material.SetTexture(CloudMapId, weatherTexture);
        }

        void UpdateWeather(int seed, double coverage, CloudRenderSettings settings)
        {
            int width = Mathf.Clamp(settings.weatherWidth, 256, 4096);
            bool stale = seed != jobSeed || Math.Abs(coverage - jobCoverage) > 1e-6 || width != jobWidth;
            if (job == null && stale)
            {
                jobSeed = seed;
                jobCoverage = coverage;
                jobWidth = width;
                job = Task.Run(() => CloudWeatherMap.Generate(seed, coverage, width));
            }
            if (job == null || !job.IsCompleted) return;
            if (job.IsFaulted)
            {
                Debug.LogError("[SpaceSim] Weather map generation failed: " + job.Exception, this);
                job = null;
                return;
            }
            weather = job.Result;
            job = null;
            if (weatherTexture != null) Destroy(weatherTexture);
            weatherTexture = new Texture2D(weather.Width, weather.Height, TextureFormat.RGBA32, true, true)
            {
                name = "Weather · " + planet.Definition.displayName,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            weatherTexture.SetPixelData(weather.Pixels, 0);
            weatherTexture.Apply(true, true);
            weather.Pixels = null; // the GPU has it; the coarse copy stays for the CPU
        }

        /// <summary>Everything derived from the planet's data and the clock: altitudes, cloud frame, wrapped camera position.</summary>
        void ComputeFrame(double renderTime, CloudRenderSettings settings, PlanetDefinition def, double pressure, double scaleHeight,
            Vector3 sunDirUnity, Vector3d cameraRelative)
        {
            var model = planet.Model;
            // Earth-like air holds clouds in the lowest scale height or so; a crushing Venus-like atmosphere lifts the deck many scale heights up.
            double dense = MathD.Clamp01(Math.Log10(Math.Max(pressure / 101325.0, 1.0)) / 2.0);
            BaseAltitude = (float)(scaleHeight * MathD.Lerp(0.2, 6.5, dense));
            TopAltitude = BaseAltitude + (float)(scaleHeight * MathD.Lerp(1.0, 3.0, dense));

            // The whole weather rotates slowly relative to the ground, about the planet's axis.
            double driftAngle = settings.driftSpeed / Math.Max(1.0, model.Radius) * renderTime;
            driftAngle %= 2.0 * Math.PI;
            Quaternion toFixed = Quaternion.Inverse(planet.UnityRotation(renderTime));
            Quaternion toCloud = Quaternion.AngleAxis((float)(driftAngle * MathD.RadToDeg), Vector3.up) * toFixed;
            Vector3 cx = toCloud * Vector3.right, cy = toCloud * Vector3.up, cz = toCloud * Vector3.forward;
            Row0 = new Vector4(cx.x, cy.x, cz.x, 0f);
            Row1 = new Vector4(cx.y, cy.y, cz.y, 0f);
            Row2 = new Vector4(cx.z, cy.z, cz.z, 0f);

            // Camera in the cloud frame, in double precision, wrapped: x and z carry the drift, a small slide makes the forms evolve.
            Vector3d cam = model.ToFixed(model.Position + cameraRelative, renderTime);
            double ux = cam.x, uy = cam.z, uz = cam.y; // planet-fixed physics axes -> Unity axes
            double c = Math.Cos(driftAngle), s = Math.Sin(driftAngle);
            double x = c * ux + s * uz + settings.evolveSpeed * renderTime;
            double z = -s * ux + c * uz + 0.37 * settings.evolveSpeed * renderTime;
            NoiseOrigin = new Vector3((float)Wrap(x), (float)Wrap(uy), (float)Wrap(z));
        }

        static double Wrap(double v) => v - NoiseWrap * Math.Floor(v / NoiseWrap);

        void UpdateSunlightAtViewer(Vector3 sunDirUnity, Vector3d cameraRelative, double radius)
        {
            SunlightAtViewer = 1f;
            Vector3 cam = CoordinateSystem.ToUnityVector(cameraRelative);
            double r = cameraRelative.Magnitude;
            double mid = 0.5 * (BaseAltitude + TopAltitude);
            if (r - radius >= mid) return;
            double cosZ = (cam.x * sunDirUnity.x + cam.y * sunDirUnity.y + cam.z * sunDirUnity.z) / r;
            if (cosZ <= 0.02) return;
            double t = (mid - (r - radius)) / Math.Max(cosZ, 0.08);
            double px = cam.x + sunDirUnity.x * t, py = cam.y + sunDirUnity.y * t, pz = cam.z + sunDirUnity.z * t;
            double len = Math.Sqrt(px * px + py * py + pz * pz);
            double dx = (Row0.x * px + Row0.y * py + Row0.z * pz) / len;
            double dy = (Row1.x * px + Row1.y * py + Row1.z * pz) / len;
            double dz = (Row2.x * px + Row2.y * py + Row2.z * pz) / len;
            float cov = weather.CoverageAt(dx, dy, dz);
            SunlightAtViewer = 1f - ShadowStrength * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.8f, cov));
        }
    }
}
