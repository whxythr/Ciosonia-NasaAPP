using System;
using UnityEngine;

namespace SpaceSim.Runtime
{
    public enum PlanetSurfaceSource
    {
        /// <summary>Deterministic procedural relief (cube-sphere quadtree LOD). Physics uses the same height function.</summary>
        Procedural,
        /// <summary>Visual mesh imported from Blender (FBX/OBJ/…); physics uses the reference sphere.</summary>
        ImportedModel,
        /// <summary>Smooth sphere at the physical radius.</summary>
        ReferenceSphere
    }

    [Serializable]
    public class ProceduralLodSettings
    {
        [Tooltip("Quads per chunk edge. Power of two keeps edge vertices bit-exact between chunks.")]
        public int chunkResolution = 32;
        [Tooltip("Deepest quadtree level. Level L chunk edge ≈ (π/2·R)/2^L; 14 ≈ 610 m (≈19 m per quad on Earth).")]
        [Range(4, 20)] public int maxLevel = 17;
        [Tooltip("A chunk splits when the camera is closer than edgeLength × this factor.")]
        [Range(0.5f, 6f)] public float splitDistanceFactor = 2.2f;
        [Tooltip("Generate chunk geometry on worker threads (disable on platforms without threads, e.g. WebGL).")]
        public bool useWorkerThreads = true;
        [Range(1, 32)] public int maxConcurrentJobs = 6;
        [Tooltip("Mesh uploads to the GPU per frame (spreads load to avoid hitches).")]
        [Range(1, 64)] public int maxMeshUploadsPerFrame = 8;
        [Tooltip("Upper bound for cached chunk geometry not currently in use [MB].")]
        [Range(16, 4096)] public int cacheBudgetMegabytes = 256;
        [Tooltip("Skirt depth as a fraction of chunk edge length (hides cracks between LOD levels).")]
        [Range(0.001f, 0.1f)] public float skirtFraction = 0.015f;
    }

    [Serializable]
    public class ImportedModelSettings
    {
        [Tooltip("Prefab of the imported model (e.g. an FBX from Blender). Purely visual.")]
        public GameObject modelPrefab;
        [Tooltip("Radius of the model in its own units. The model is scaled so this matches the PHYSICAL radius — the model never defines the radius.")]
        [Min(1e-6f)] public float modelRadius = 1f;
        [Tooltip("Extra rotation to align the model's pole with the planet's spin axis (+Y in Unity model space = north).")]
        public Vector3 rotationOffsetEuler = Vector3.zero;
    }

    [Serializable]
    public class AtmosphereRenderSettings
    {
        public bool enabled = true;
        [Tooltip("Solar irradiance scale used by the scattering shader (tone-mapped afterwards).")]
        [Min(0f)] public float sunIntensity = 22f;
        [Tooltip("Exposure applied to scattered light before tone mapping.")]
        [Min(0f)] public float exposure = 0.4f;
        [Tooltip("Samples along the view ray. Each costs one table lookup, so this is cheap (the old nested light march is gone).")]
        [Range(4, 64)] public int viewSamples = 28;
        [Tooltip("Strength of the isotropic fill that stands in for second and higher orders of scattering (softer twilight).")]
        [Range(0f, 4f)] public float multipleScattering = 1f;
        public CloudRenderSettings clouds = new CloudRenderSettings();
    }

    /// <summary>The look of the cloud deck. How much of the planet is cloudy lives with the planet's data (generation.cloudCoverage).</summary>
    [Serializable]
    public class CloudRenderSettings
    {
        public bool enabled = true;
        [Tooltip("Extinction of the densest cloud [1/m]. Higher = harder edges, darker undersides.")]
        [Range(0.001f, 0.08f)] public float extinction = 0.012f;
        [Tooltip("Primary ray steps per pixel at close range (coarse for distant bodies). The main cost of the clouds.")]
        [Range(8, 96)] public int primarySteps = 34;
        [Range(1, 8)] public int lightSteps = 4;
        [Tooltip("Sky light on the clouds (their undersides and shadowed flanks).")]
        [Range(0f, 8f)] public float ambient = 2.2f;
        [Tooltip("Opacity of the thin high ice clouds (0 = none).")]
        [Range(0f, 1f)] public float cirrus = 0.45f;
        [Tooltip("How much cloud shadows darken the ground (0..1).")]
        [Range(0f, 1f)] public float shadowStrength = 0.75f;
        [Tooltip("Slow drift of the weather relative to the ground [m/s].")]
        public double driftSpeed = 11.0;
        [Tooltip("Slide of the small forms inside the weather [m/s]: makes the clouds change shape.")]
        public double evolveSpeed = 4.0;
        [Tooltip("Width of the weather map [texels]; height is half of it. 2048 ≈ 20 km per texel on an Earth-sized planet.")]
        public int weatherWidth = 2048;
    }

    [Serializable]
    public class LaunchSiteSettings
    {
        public string name = "Стартовая площадка 1";
        public double latitudeDegrees = 45.965;
        public double longitudeDegrees = 63.305;
        [Tooltip("Radius of the concrete pad [m] (visual).")]
        [Min(1f)] public float padRadius = 40f;
        [Tooltip("Terrain is flattened within this distance [m] and blends back over the same distance.")]
        public double flattenRadius = 600.0;
        [Tooltip("The pad is kept at least this high above sea level [m] (builds an islet if the site is at sea).")]
        public double minHeightAboveSeaLevel = 15.0;
    }
}
