using System;
using SpaceSim.Mathematics;
using SpaceSim.Procedural;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Deterministic sky at infinity, fixed in the inertial frame: a star field (one static mesh, each star projected with the
    /// camera rotation only) and the diffuse band of the home galaxy (<c>SpaceSim/MilkyWay</c>, procedural).
    /// <para>Stars follow the observed counts: about three times more stars per magnitude step fainter, and the faint ones
    /// crowd towards the galactic plane like the band behind them. Inside air they scintillate (turbulence bends the light of a
    /// point source); above the atmosphere they burn steadily.</para>
    /// Visibility models eye/camera adaptation: in daylight air the bright sky hides stars and the band; at night or above
    /// the atmosphere they appear. (Purely visual — the atmosphere pass additionally dims them by transmittance.)
    /// </summary>
    [DefaultExecutionOrder(130)]
    public sealed class StarfieldController : MonoBehaviour
    {
        [SerializeField] int starSeed = 1977;
        [SerializeField] int starCount = 20000;
        [Tooltip("Faintest magnitude generated.")]
        [SerializeField] float limitingMagnitude = 7.4f;
        [SerializeField] float brightness = 2.5f;
        [SerializeField] float pixelSize = 1.6f;
        [Tooltip("Surface brightness of the galactic band relative to the stars (0 = off).")]
        [SerializeField] float milkyWayIntensity = 0.35f;

        /// <summary>Brightest magnitude generated (Sirius-like).</summary>
        const double BrightestMagnitude = -1.5;
        /// <summary>Star counts grow ≈ ×3 per magnitude (d log N / dm ≈ 0.48 near the Sun).</summary>
        static readonly double CountGrowth = Math.Log(3.0);
        /// <summary>Exponent applied to the flux ratio for display (1 = linear, as before).</summary>
        const double DisplayGamma = 0.6;

        /// <summary>
        /// The galactic frame in world (inertial) directions: the band is tilted ~60° to the planets' orbital plane, as the
        /// Milky Way is to the ecliptic. <see cref="GalacticCenter"/> is perpendicular to <see cref="GalacticPole"/>.
        /// </summary>
        public static readonly Vector3 GalacticPole = new Vector3(0.44f, 0.47f, 0.76f).normalized;
        public static Vector3 GalacticCenter => Vector3.ProjectOnPlane(new Vector3(0.88f, -0.36f, -0.30f), GalacticPole).normalized;

        StarSystemRuntime system;
        FloatingOrigin origin;
        SolarLightingController sun;
        Material material;
        Mesh mesh;
        Material galaxyMaterial;
        Mesh skySphere;

        /// <summary>Current visibility 0..1 (exposed for HUD/tests).</summary>
        public float Visibility { get; private set; }
        /// <summary>Scintillation strength now, 0 above the atmosphere … 1 at the bottom of a full air column.</summary>
        public float Twinkle { get; private set; }
        /// <summary>The star mesh and its shader, reused by the 3D map for the same sky.</summary>
        public Mesh StarMesh => mesh;
        public Shader StarShader => material != null ? material.shader : null;
        /// <summary>Band brightness (bridge: <c>gfx milky X</c>).</summary>
        public float MilkyWayIntensity { get => milkyWayIntensity; set => milkyWayIntensity = Mathf.Max(0f, value); }

        public void Initialize(StarSystemRuntime starSystem, FloatingOrigin floatingOrigin, SolarLightingController solar, Shader shader)
        {
            system = starSystem;
            origin = floatingOrigin;
            sun = solar;
            mesh = BuildMesh();
            material = new Material(shader) { name = "Starfield (runtime)" };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            galaxyMaterial = CreateMilkyWayMaterial(0f);
            if (galaxyMaterial != null) AddMilkyWay(transform, gameObject.layer, galaxyMaterial);
        }

        /// <summary>The galactic band's material (null if the shader is missing), with the galactic frame set.</summary>
        public static Material CreateMilkyWayMaterial(float brightnessNow)
        {
            Material m = ProceduralMeshes.EffectMaterial("SpaceSim/MilkyWay");
            if (m == null) return null;
            m.name = "MilkyWay (runtime)";
            m.SetVector("_GalPole", GalacticPole);
            m.SetVector("_GalCenter", GalacticCenter);
            m.SetFloat("_Brightness", brightnessNow);
            return m;
        }

        /// <summary>Puts the band (a sphere of directions) under <paramref name="parent"/>; used by the flight sky and the 3D map.</summary>
        public GameObject AddMilkyWay(Transform parent, int layer, Material galaxy)
        {
            if (skySphere == null)
            {
                skySphere = ProceduralMeshes.UvSphere(64, 32, "SkySphere");
                skySphere.bounds = new Bounds(Vector3.zero, Vector3.one * 2e10f);
            }
            var go = new GameObject("MilkyWay") { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = skySphere;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = galaxy;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (mesh != null) Destroy(mesh);
            if (galaxyMaterial != null) Destroy(galaxyMaterial);
            if (skySphere != null) Destroy(skySphere);
        }

        /// <summary>Galactic latitude [rad] and closeness to the galactic centre (1 towards it, ~0 opposite) of a world direction.</summary>
        static void Galactic(Vector3 dir, out float latitude, out float towardsCentre)
        {
            Vector3 c1 = GalacticCenter, c2 = Vector3.Cross(GalacticPole, c1);
            latitude = Mathf.Asin(Mathf.Clamp(Vector3.Dot(dir, GalacticPole), -1f, 1f));
            float l = Mathf.Atan2(Vector3.Dot(dir, c2), Vector3.Dot(dir, c1));
            towardsCentre = Mathf.Exp(-l * l / (2f * 0.9f * 0.9f));
        }

        Mesh BuildMesh()
        {
            var rng = new DeterministicRandom(DeterministicRandom.Mix((ulong)(uint)starSeed, 0x57A75UL));
            int n = Mathf.Max(1, starCount);
            var vertices = new Vector3[n * 4];
            var colors = new Color[n * 4];
            var uv = new Vector2[n * 4];
            var size = new Vector2[n * 4];
            var tris = new int[n * 6];
            var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            double span = Math.Exp(CountGrowth * (limitingMagnitude - BrightestMagnitude)) - 1.0;
            for (int s = 0; s < n; s++)
            {
                // Magnitude from the cumulative count N(<m) ∝ e^(k·m): mostly faint stars, a handful of bright ones.
                double mag = BrightestMagnitude + Math.Log(1.0 + rng.NextDouble() * span) / CountGrowth;
                // Bright stars are near and spread evenly; faint ones (distant) crowd towards the galactic plane.
                double crowding = MathD.SmoothStep(3.0, 6.5, mag);
                Vector3 dir;
                while (true)
                {
                    dir = CoordinateSystem.ToUnityVector(rng.UnitVector());
                    Galactic(dir, out float b, out float centre);
                    double plane = Math.Exp(-(b * b) / (0.2 * 0.2)) * (0.45 + 0.55 * centre);
                    double accept = 1.0 - crowding * (1.0 - (0.22 + 0.78 * plane));
                    if (rng.NextDouble() <= accept) break;
                }
                // Displayed brightness: the physical flux ratio (×2.512 per magnitude) compressed like the eye's / a camera's
                // response, so 6th-magnitude stars are faint dots rather than invisible.
                float flux = (float)Math.Pow(10.0, -0.4 * (mag - 1.0) * DisplayGamma);
                float temperature = (float)(3000.0 + 9000.0 * rng.NextDouble() * rng.NextDouble());
                Color c = TemperatureToColor(temperature) * Mathf.Min(flux, 6f) * 0.25f;
                float px = Mathf.Lerp(0.8f, 2.2f, Mathf.Clamp01((float)((limitingMagnitude - mag) / 7.0)));
                float phase = (float)rng.NextDouble();
                for (int k = 0; k < 4; k++)
                {
                    int v = s * 4 + k;
                    vertices[v] = dir;
                    colors[v] = c;
                    uv[v] = corners[k];
                    size[v] = new Vector2(px, phase);
                }
                int t = s * 6, b0 = s * 4;
                tris[t] = b0; tris[t + 1] = b0 + 2; tris[t + 2] = b0 + 1;
                tris[t + 3] = b0; tris[t + 4] = b0 + 3; tris[t + 5] = b0 + 2;
            }
            var m = new Mesh { name = "Starfield", indexFormat = n * 4 > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.vertices = vertices;
            m.colors = colors;
            m.uv = uv;
            m.uv2 = size;
            m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2e10f);
            return m;
        }

        /// <summary>Approximate blackbody colour (Tanner Helland fit), normalised to max component 1.</summary>
        static Color TemperatureToColor(float kelvin)
        {
            float t = kelvin / 100f;
            float r = t <= 66f ? 1f : Mathf.Clamp01(1.292936f * Mathf.Pow(t - 60f, -0.1332047f));
            float g = t <= 66f ? Mathf.Clamp01(0.3900816f * Mathf.Log(t) - 0.6318414f) : Mathf.Clamp01(1.129891f * Mathf.Pow(t - 60f, -0.0755148f));
            float b = t >= 66f ? 1f : (t <= 19f ? 0f : Mathf.Clamp01(0.5432068f * Mathf.Log(t - 10f) - 1.1962541f));
            float max = Mathf.Max(r, Mathf.Max(g, b));
            return new Color(r / max, g / max, b / max, 1f);
        }

        void LateUpdate()
        {
            if (system == null || system.Bodies.Count == 0 || system.Active.Model == null) return;
            var model = system.Active.Model;
            Vector3d rel = origin.ViewerPosition - model.Position;
            double rho0 = model.Atmosphere.ReferenceDensity;
            // Fraction of the air column above the viewer (exact for an exponential atmosphere).
            double column = rho0 > 0.0 ? model.Atmosphere.Density(rel.Magnitude - model.Radius) / rho0 : 0.0;
            double sunUp = sun.SunElevationSineAtViewer;
            double daylight = MathD.SmoothStep(-0.20, 0.05, sunUp);
            double skyBrightness = Math.Min(1.0, column) * daylight;
            Visibility = 1f - (float)MathD.SmoothStep(0.0003, 0.03, skyBrightness);
            Twinkle = (float)MathD.SmoothStep(0.002, 0.3, Math.Min(1.0, column));

            material.SetFloat("_Brightness", brightness * Visibility);
            material.SetFloat("_PixelSize", pixelSize);
            material.SetFloat("_Twinkle", Twinkle);
            // The band is a much fainter, extended glow: it fades out in brighter skies than the stars do.
            if (galaxyMaterial != null)
                galaxyMaterial.SetFloat("_Brightness", milkyWayIntensity * Visibility * Visibility);
        }
    }
}
