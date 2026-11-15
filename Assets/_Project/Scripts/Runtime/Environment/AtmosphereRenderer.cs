using SpaceSim.Mathematics;
using SpaceSim.Physics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// One per planet: renders that planet's atmosphere shell, runs its cloud deck, and feeds every material that needs this planet's light
    /// (surface, sky, clouds, exhaust smoke) with the same constants — star direction, centre, scale, scattering, transmittance table, cloud
    /// frame — so that they all light up consistently (see SpaceSimLighting.hlsl). Rayleigh density follows the PHYSICAL profile via its
    /// (effective) scale height; the shell radius is the visual bound derived from it — not the "space boundary".
    /// Rendering never feeds back into physics.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class AtmosphereRenderer : MonoBehaviour
    {
        // Sky
        static readonly int CameraPlanetPos = Shader.PropertyToID("_CameraPlanetPos");
        static readonly int CameraHeight = Shader.PropertyToID("_CameraHeight");
        static readonly int BetaRayleigh = Shader.PropertyToID("_BetaRayleigh");
        static readonly int BetaMie = Shader.PropertyToID("_BetaMie");
        static readonly int MieExtinction = Shader.PropertyToID("_MieExtinction");
        static readonly int ScaleHeightR = Shader.PropertyToID("_ScaleHeightR");
        static readonly int ScaleHeightM = Shader.PropertyToID("_ScaleHeightM");
        static readonly int MieG = Shader.PropertyToID("_MieG");
        static readonly int SunIntensity = Shader.PropertyToID("_SunIntensity");
        static readonly int Exposure = Shader.PropertyToID("_Exposure");
        static readonly int MultiScatter = Shader.PropertyToID("_MultiScatter");
        static readonly int ViewSamples = Shader.PropertyToID("_ViewSamples");
        static readonly int HazeScattering = Shader.PropertyToID("_HazeScattering");
        static readonly int HazeAltitude = Shader.PropertyToID("_HazeAltitude");
        static readonly int HazeThickness = Shader.PropertyToID("_HazeThickness");
        // Light model
        static readonly int SSunDir = Shader.PropertyToID("_SpaceSimSunDir");
        static readonly int SSunColor = Shader.PropertyToID("_SpaceSimSunColor");
        static readonly int SPlanetCenter = Shader.PropertyToID("_SpaceSimPlanetCenter");
        static readonly int SPlanetRadius = Shader.PropertyToID("_SpaceSimPlanetRadius");
        static readonly int SRenderScale = Shader.PropertyToID("_SpaceSimRenderScale");
        static readonly int SHasAtmosphere = Shader.PropertyToID("_SpaceSimHasAtmosphere");
        static readonly int SSkyAmbient = Shader.PropertyToID("_SpaceSimSkyAmbient");
        static readonly int SRadiusM = Shader.PropertyToID("_SpaceSimRadiusM");
        static readonly int SShellM = Shader.PropertyToID("_SpaceSimShellM");
        static readonly int SAirScaleHeight = Shader.PropertyToID("_SpaceSimAirScaleHeight");
        static readonly int STransmittance = Shader.PropertyToID("_SpaceSimTransmittance");
        // Clouds on the ground
        static readonly int SCloudShadow = Shader.PropertyToID("_SpaceSimCloudShadow");
        static readonly int SCloudBase = Shader.PropertyToID("_SpaceSimCloudBase");
        static readonly int SCloudTop = Shader.PropertyToID("_SpaceSimCloudTop");
        static readonly int SCloudNoiseOrigin = Shader.PropertyToID("_SpaceSimCloudNoiseOrigin");
        static readonly int SCloudRow0 = Shader.PropertyToID("_SpaceSimCloudRow0");
        static readonly int SCloudRow1 = Shader.PropertyToID("_SpaceSimCloudRow1");
        static readonly int SCloudRow2 = Shader.PropertyToID("_SpaceSimCloudRow2");
        static readonly int SCloudMap = Shader.PropertyToID("_SpaceSimCloudMap");
        static readonly int LodSplit = Shader.PropertyToID("_LodSplitFactor");

        PlanetRuntime planet;
        FloatingOrigin origin;
        StarSystemRuntime system;
        Material surfaceMaterial;
        Material material;
        MeshRenderer meshRenderer;
        Mesh shellMesh;
        Texture2D transmittance;
        CloudLayerRenderer clouds;
        bool lutDirty = true;

        // This frame's values, shared by every material of the planet.
        bool hasAir;
        Vector4 rayleigh, hazeBeta;
        float mieExt, hR, hM, scale, radius, shell;
        Vector3 center, sunDir, camPos;
        float camHeight;

        /// <summary>The planet's cloud deck (always present; hidden without air).</summary>
        public CloudLayerRenderer Clouds => clouds;

        /// <summary>Mesh inflation so the polygonal shell fully covers the analytic sphere.</summary>
        const float ShellInflation = 1.03f;

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin floatingOrigin, StarSystemRuntime starSystem,
            Shader shader, Material planetSurfaceMaterial)
        {
            planet = planetRuntime;
            origin = floatingOrigin;
            system = starSystem;
            surfaceMaterial = planetSurfaceMaterial;
            material = new Material(shader) { name = "Atmosphere · " + planet.Definition.displayName };
            shellMesh = ProceduralMeshes.UvSphere(96, 48, "AtmosphereShell");
            shellMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f * ShellInflation);
            gameObject.AddComponent<MeshFilter>().sharedMesh = shellMesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            var deck = new GameObject("Clouds");
            deck.transform.SetParent(transform.parent, false);
            clouds = deck.AddComponent<CloudLayerRenderer>();
            clouds.Initialize(planet, origin, system, shellMesh);
            planet.ModelChanged += OnModelChanged;
        }

        void OnModelChanged(SpaceSim.Planets.PlanetModel model) => lutDirty = true;

        void OnDestroy()
        {
            if (planet != null) planet.ModelChanged -= OnModelChanged;
            if (material != null) Destroy(material);
            if (shellMesh != null) Destroy(shellMesh);
            if (transmittance != null) Destroy(transmittance);
        }

        /// <summary>
        /// Scale height that reproduces the profile's density at 5 km (exact for the exponential model, a fit
        /// for tabulated ones). The shader's exp(−h/H) is the only approximation of the physical profile.
        /// </summary>
        public static double EffectiveScaleHeight(IAtmosphereProfile profile)
        {
            if (profile is ExponentialAtmosphere e) return e.ScaleHeight;
            double rho0 = profile.ReferenceDensity;
            double rho = profile.Density(5000.0);
            if (rho0 <= 0.0 || rho <= 0.0 || rho >= rho0) return 8500.0;
            return -5000.0 / System.Math.Log(rho / rho0);
        }

        /// <summary>
        /// The colour of a planet's air relative to Earth's: Earth-like optics give white, sulphur haze a warm orange. Multiplies the sky light,
        /// which was a single blue for every world before.
        /// </summary>
        public static Vector3 AirTint(AtmosphereOpticalProperties optics)
        {
            Vector3d b = optics.rayleighScattering;
            double r = b.x / 5.802e-6, g = b.y / 13.558e-6, bl = b.z / 33.1e-6;
            double max = System.Math.Max(r, System.Math.Max(g, bl));
            if (max <= 0.0) return new Vector3(1f, 1f, 1f);
            return new Vector3((float)(r / max), (float)(g / max), (float)(bl / max));
        }

        void RebuildTransmittance()
        {
            lutDirty = false;
            if (transmittance != null)
            {
                Destroy(transmittance);
                transmittance = null;
            }
            var model = planet.Model;
            if (model == null || planet.VisualShellHeight <= 0.0 || model.Atmosphere.ReferenceDensity <= 0.0) return;
            // The table must describe the air the sky pass draws, including a planet's tint.
            AtmosphereOpticalProperties optics = planet.Definition.optics.Clone();
            var tint = planet.Definition.generation.atmosphereTint;
            Vector3d br = optics.rayleighScattering;
            optics.rayleighScattering = new Vector3d(br.x * tint.r, br.y * tint.g, br.z * tint.b);
            transmittance = AtmosphereTransmittanceLut.Build(model.Atmosphere, optics, model.Radius, planet.VisualShellHeight, planet.Definition.displayName);
        }

        void LateUpdate()
        {
            if (planet == null || planet.Model == null) return;
            SpaceSimNoiseTextures.Tick();
            if (lutDirty) RebuildTransmittance();

            var def = planet.Definition;
            var model = planet.Model;
            hasAir = def.atmosphereRendering.enabled && def.optics.visible && model.Atmosphere.ReferenceDensity > 0.0 && planet.VisualShellHeight > 0.0;

            AtmosphereOpticalProperties optics = def.optics;
            var tint = def.generation.atmosphereTint;
            Vector3d br = optics.rayleighScattering;
            rayleigh = new Vector4((float)br.x * tint.r, (float)br.y * tint.g, (float)br.z * tint.b, 0f);
            mieExt = (float)(optics.mieScattering * optics.mieExtinctionRatio);
            hR = (float)EffectiveScaleHeight(model.Atmosphere);
            hM = Mathf.Max(1f, (float)optics.mieScaleHeight);
            scale = (float)planet.RenderScale;
            center = planet.UnityCenter(origin);
            sunDir = CoordinateSystem.ToUnityVector(system.SunDirectionFrom(model.Position));
            radius = (float)model.Radius;
            shell = hasAir ? (float)planet.VisualShellHeight : 0f;
            Vector3d camRel = origin.ViewerPosition - model.Position;
            camPos = CoordinateSystem.ToUnityVector(camRel);
            camHeight = (float)(camRel.Magnitude - model.Radius);
            Vector3d bh = optics.HasHaze ? optics.hazeScattering : Vector3d.Zero;
            hazeBeta = new Vector4((float)bh.x, (float)bh.y, (float)bh.z, 0f);

            clouds.Tick(system.RenderTime, hasAir, sunDir, planet.VisualShellHeight, hR, camRel);

            if (surfaceMaterial != null)
            {
                ApplyLighting(surfaceMaterial);
                surfaceMaterial.SetFloat(LodSplit, def.lod.splitDistanceFactor);
            }
            if (clouds.Material != null) ApplyLighting(clouds.Material);

            meshRenderer.enabled = hasAir;
            if (!hasAir) return;
            transform.SetPositionAndRotation(center, Quaternion.identity);
            transform.localScale = Vector3.one * (float)((model.Radius + planet.VisualShellHeight) * ShellInflation * planet.RenderScale);
            ApplyLighting(material);
            material.SetFloat(ViewSamples, def.atmosphereRendering.viewSamples);
        }

        /// <summary>Sets this planet's light, air and cloud constants on a material (any shader using the shared light model).</summary>
        public void ApplyLighting(Material m)
        {
            var def = planet.Definition;
            var optics = def.optics;
            Color sun = system.StarLightColor;
            Vector3 airTint = AirTint(optics);
            Color sky = system.SkyAmbientFor(planet);

            m.SetVector(SSunDir, sunDir);
            m.SetVector(SSunColor, new Vector4(sun.r, sun.g, sun.b, 0f));
            m.SetVector(SPlanetCenter, center);
            m.SetFloat(SPlanetRadius, radius * scale);
            m.SetFloat(SRenderScale, scale);
            m.SetFloat(SHasAtmosphere, hasAir ? 1f : 0f);
            m.SetVector(SSkyAmbient, new Vector4(sky.r * airTint.x, sky.g * airTint.y, sky.b * airTint.z, 0f));
            m.SetFloat(SRadiusM, radius);
            m.SetFloat(SShellM, shell);
            m.SetFloat(SAirScaleHeight, hR);
            m.SetTexture(STransmittance, transmittance != null ? (Texture)transmittance : Texture2D.whiteTexture);

            m.SetFloat(SCloudShadow, clouds.ShadowStrength);
            m.SetFloat(SCloudBase, clouds.BaseAltitude);
            m.SetFloat(SCloudTop, clouds.TopAltitude);
            m.SetVector(SCloudNoiseOrigin, clouds.NoiseOrigin);
            m.SetVector(SCloudRow0, clouds.Row0);
            m.SetVector(SCloudRow1, clouds.Row1);
            m.SetVector(SCloudRow2, clouds.Row2);
            if (clouds.WeatherTexture != null) m.SetTexture(SCloudMap, clouds.WeatherTexture);

            m.SetVector(CameraPlanetPos, camPos);
            m.SetFloat(CameraHeight, camHeight);
            m.SetVector(BetaRayleigh, rayleigh);
            m.SetFloat(BetaMie, (float)optics.mieScattering);
            m.SetFloat(MieExtinction, mieExt);
            m.SetFloat(ScaleHeightR, hR);
            m.SetFloat(ScaleHeightM, hM);
            m.SetFloat(MieG, (float)optics.mieAnisotropy);
            m.SetFloat(SunIntensity, def.atmosphereRendering.sunIntensity);
            m.SetFloat(Exposure, def.atmosphereRendering.exposure);
            m.SetFloat(MultiScatter, def.atmosphereRendering.multipleScattering);
            m.SetVector(HazeScattering, hazeBeta);
            m.SetFloat(HazeAltitude, (float)optics.hazeAltitude);
            m.SetFloat(HazeThickness, Mathf.Max(1f, (float)optics.hazeThickness));
        }
    }
}
