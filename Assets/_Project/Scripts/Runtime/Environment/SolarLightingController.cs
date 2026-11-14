using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The star as seen from the camera: the directional light, the visible disk (true angular size from the real
    /// distance and radius), and ambient light for objects near the camera. The light reaching those objects is
    /// attenuated by the active body's atmosphere along the actual ray to the star and is zero in its shadow.
    /// Planet surfaces get their own per-planet star direction from AtmosphereRenderer.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public sealed class SolarLightingController : MonoBehaviour
    {
        SphericalHarmonicsL2 ambientProbe;
        /// <summary>Sky light on standard-lit objects (rocket, tower, pad, hangar) relative to the planet's sky ambient.</summary>
        const float ObjectSkyLight = 1.5f;
        /// <summary>
        /// Sunlight thrown back by the ground onto the side of an object facing away from the Sun: about half the hemisphere is
        /// ground with an albedo of ~0.2–0.3, so ≈ 0.1 of the sunlight falling on it. It is warm and neutral, unlike the sky term,
        /// so the shadow side reads grey-blue instead of black or saturated blue.
        /// </summary>
        const float GroundBounce = 0.1f;

        [Tooltip("Ambient on the night side / in space (starlight, scattered light).")]
        [SerializeField] Color ambientFloor = new Color(0.005f, 0.006f, 0.008f);

        StarSystemRuntime system;
        FloatingOrigin origin;
        Light sunLight;
        Material diskMaterial;
        Mesh diskMesh;

        public Light Light => sunLight;
        /// <summary>Unit vector from the camera towards the star (simulation frame).</summary>
        public Vector3d SunDirection { get; private set; } = Vector3d.UnitX;
        public Vector3 SunDirectionUnity => CoordinateSystem.ToUnityVector(SunDirection);
        /// <summary>Distance from the camera to the star's centre [m].</summary>
        public double StarDistance { get; private set; }
        /// <summary>Spectral transmittance of starlight at the camera (0 in the active body's shadow).</summary>
        public Vector3d TransmittanceAtViewer { get; private set; } = new Vector3d(1, 1, 1);
        /// <summary>Sine of the star's elevation above the camera's local horizontal plane (active body).</summary>
        public double SunElevationSineAtViewer { get; private set; }

        public void Initialize(StarSystemRuntime starSystem, FloatingOrigin floatingOrigin, Shader sunDiskShader)
        {
            system = starSystem;
            origin = floatingOrigin;

            sunLight = gameObject.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowStrength = 1f;
            sunLight.shadowBias = 0.05f;
            sunLight.shadowNormalBias = 0.4f;
            RenderSettings.sun = sunLight;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;

            if (sunDiskShader != null)
            {
                var disk = new GameObject("StarDisk");
                disk.transform.SetParent(transform.parent, false);
                diskMesh = new Mesh
                {
                    name = "StarDiskQuad",
                    vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero },
                    uv = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) },
                    triangles = new[] { 0, 2, 1, 0, 3, 2 },
                    bounds = new Bounds(Vector3.zero, Vector3.one * 2e10f)
                };
                disk.AddComponent<MeshFilter>().sharedMesh = diskMesh;
                diskMaterial = new Material(sunDiskShader) { name = "StarDisk (runtime)" };
                var mr = disk.AddComponent<MeshRenderer>();
                mr.sharedMaterial = diskMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        void OnDestroy()
        {
            if (diskMaterial != null) Destroy(diskMaterial);
            if (diskMesh != null) Destroy(diskMesh);
        }

        void LateUpdate()
        {
            if (system == null || system.Bodies.Count == 0) return; // empty after a script reload in Play mode
            PlanetRuntime planet = system.Active;
            if (planet.Model == null) return;
            var model = planet.Model;
            Vector3d viewer = origin.ViewerPosition;
            Vector3d toStar = system.StarPosition - viewer;
            StarDistance = toStar.Magnitude;
            SunDirection = toStar / StarDistance;
            Vector3 dirUnity = SunDirectionUnity;
            transform.rotation = Quaternion.LookRotation(-dirUnity);

            Vector3d viewerRel = viewer - model.Position;
            SunElevationSineAtViewer = Vector3d.Dot(viewerRel.Normalized, SunDirection);
            double shellRadius = model.Radius + planet.VisualShellHeight;
            TransmittanceAtViewer = AtmosphereOptics.Transmittance(model.Atmosphere, planet.Definition.optics,
                model.Radius, shellRadius, viewerRel, SunDirection, 32);

            Color starColor = new Color(system.Star.color.r, system.Star.color.g, system.Star.color.b);
            Vector3d t = TransmittanceAtViewer;
            float peak = (float)Math.Max(t.x, Math.Max(t.y, t.z));
            // The same cloud deck that shades the ground shades what stands on it (rocket, tower, hangar).
            var body = system.ActiveBody;
            CloudLayerRenderer clouds = body.Atmosphere != null ? body.Atmosphere.Clouds : null;
            float underCloud = clouds != null ? clouds.SunlightAtViewer : 1f;
            sunLight.intensity = system.StarLightIntensity * peak * underCloud;
            sunLight.color = peak > 1e-4f
                ? new Color(starColor.r * (float)t.x / peak, starColor.g * (float)t.y / peak, starColor.b * (float)t.z / peak)
                : starColor;
            sunLight.enabled = peak > 1e-4f;

            // Flat ambient for standard-lit objects near the camera: sky light while inside lit air.
            double rho0 = model.Atmosphere.ReferenceDensity;
            double column = rho0 > 0.0 ? model.Atmosphere.Density(viewerRel.Magnitude - model.Radius) / rho0 : 0.0;
            float day = Mathf.Clamp01((float)SunElevationSineAtViewer * 2.5f + 0.25f) * (float)Math.Min(1.0, column);
            Color sky = system.SkyAmbientFor(planet);
            Vector3 air = AtmosphereRenderer.AirTint(planet.Definition.optics);
            Color bounce = sunLight.enabled
                ? sunLight.color * (sunLight.intensity * Mathf.Max(0f, (float)SunElevationSineAtViewer) * GroundBounce)
                : Color.black;
            Color ambient = ambientFloor + new Color(sky.r * air.x, sky.g * air.y, sky.b * air.z) * (day * ObjectSkyLight) + bounce;
            ambient.a = 1f;
            RenderSettings.ambientLight = ambient;
            // URP Lit reads the ambient from the SH probe, which a runtime change of ambientLight does not rebuild: set it directly.
            ambientProbe.Clear();
            ambientProbe.AddAmbientLight(RenderSettings.ambientLight);
            RenderSettings.ambientProbe = ambientProbe;

            if (diskMaterial != null)
            {
                diskMaterial.SetVector("_SunDir", dirUnity);
                diskMaterial.SetFloat("_AngularRadius", (float)Math.Asin(Math.Min(1.0, system.Star.radius / StarDistance)));
                // The wide halo is mostly forward scattering by the air (aerosols): strong in it, faint in vacuum, where only the
                // eye's/lens' own glare and the diffraction rays remain.
                float inAir = (float)Math.Min(1.0, column);
                diskMaterial.SetFloat("_HaloIntensity", Mathf.Lerp(0.08f, 0.35f, inAir));
                diskMaterial.SetFloat("_GlowIntensity", Mathf.Lerp(1.2f, 2.5f, inAir));
                // The sky pass dims everything behind it by the MEAN transmittance; the colour of the low Sun comes from here.
                float mean = (float)((t.x + t.y + t.z) / 3.0);
                diskMaterial.SetColor("_Color", mean > 1e-4f
                    ? new Color(starColor.r * (float)t.x / mean, starColor.g * (float)t.y / mean, starColor.b * (float)t.z / mean)
                    : starColor);
            }
        }
    }
}
