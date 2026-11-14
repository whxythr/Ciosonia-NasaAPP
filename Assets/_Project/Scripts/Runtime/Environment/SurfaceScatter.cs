using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Procedural;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Small ground objects around the viewer so the terrain near the pad, a rover or an astronaut is not an empty flat colour:
    /// boulders and stones of several shapes, flat slabs, pebble clusters and, on green land, low cushion shrubs (never trees).
    ///
    /// Placement is deterministic: the planet is cut into ≈ <see cref="CellSize"/>-metre cells on a latitude/longitude grid
    /// (longitude cells shrink towards the poles so every cell has the same area), and each cell's content comes from a hash of
    /// the planet's seed and the cell index — the same rocks are in the same places every visit. What a cell holds depends on the
    /// surface sample there: none in water, many on bare rock and desert, few on grass and snow, none on the launch pad and the
    /// hangar apron. The colour of each stone follows the ground it lies on.
    ///
    /// Purely visual (no colliders, nothing feeds back into physics). Instances are rebuilt on a worker thread when the viewer
    /// has moved <see cref="RebuildDistance"/> along the ground and are drawn with GPU instancing relative to an anchor that rides
    /// on the rotating planet, so float precision never suffers.
    /// </summary>
    [DefaultExecutionOrder(130)]
    public sealed class SurfaceScatter : MonoBehaviour
    {
        /// <summary>Edge of a placement cell [m].</summary>
        public const double CellSize = 8.0;
        /// <summary>Objects are kept up to this ground distance from the viewer [m].</summary>
        public const double Radius = 520.0;
        const double RebuildDistance = 50.0;
        /// <summary>Above this height over the ground nothing is drawn (the objects are smaller than a pixel) [m].</summary>
        const double MaxViewerAltitude = 3000.0;
        /// <summary>No objects this close to the pad centre (pad slab, tower, masts) [m].</summary>
        const double PadClearance = 55.0;
        /// <summary>No objects this close to the hangar centre (building and its apron) [m].</summary>
        const double HangarClearance = 75.0;
        const int MaxPerCall = 1023;
        const int RockVariants = 5;
        const int Palette = 10;

        enum Kind { Rock, Slab, Pebbles, Shrub }

        struct Instance
        {
            public int Mesh;
            public int Material;
            public bool Shadow;
            public Matrix4x4 Local;
        }

        sealed class Batch
        {
            public Mesh Mesh;
            public Material Material;
            public bool Shadow;
            public Matrix4x4[] Local;
            public Matrix4x4[] World;
            public int Count;
        }

        PlanetRuntime planet;
        FloatingOrigin origin;
        SimulationController simulation;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Material> materials = new List<Material>();
        Color[] paletteColors;
        readonly List<Batch> batches = new List<Batch>();
        Vector3d anchorFixed;
        Vector3d builtFoot;
        bool hasBuild;
        Task<Result> job;
        int shrubMaterial;
        double padCosClearance, hangarCosClearance;
        Vector3d padDirection, hangarDirection;
        bool enabledForBody;

        sealed class Result
        {
            public Vector3d Anchor;
            public Vector3d Foot;
            public List<Instance> Instances;
        }

        /// <summary>Number of objects drawn now (bridge diagnostics).</summary>
        public int Count { get; private set; }

        public string Describe() =>
            "scatter enabled=" + enabledForBody + " objects=" + Count + " batches=" + batches.Count + (job != null ? " (rebuilding)" : "");

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin floatingOrigin, SimulationController sim, Shader litShader)
        {
            planet = planetRuntime;
            origin = floatingOrigin;
            simulation = sim;
            var def = planet.Definition;
            enabledForBody = def.generation != null && def.generation.surfaceStyle != SurfaceStyle.GasGiant && litShader != null;
            if (!enabledForBody) return;

            var site = def.launchSite;
            padDirection = PlanetRotation.DirectionFromLatLon(site.latitudeDegrees * MathD.DegToRad, site.longitudeDegrees * MathD.DegToRad);
            double meanRadius = Math.Max(1.0, def.physical.meanRadius);
            padCosClearance = Math.Cos(PadClearance / meanRadius);
            hangarCosClearance = Math.Cos(HangarClearance / meanRadius);
            PlanetRotation.LocalBasis(padDirection, out _, out Vector3d padNorth);
            hangarDirection = (padDirection * meanRadius - padNorth * HangarBuilding.DistanceFromPad).Normalized;

            int seed = def.generation.seed;
            for (int i = 0; i < RockVariants; i++) meshes.Add(RockMesh(seed * 31 + i, 0.55f + 0.1f * i, 0.30f));
            meshes.Add(RockMesh(seed * 31 + 11, 0.28f, 0.22f));   // slab
            meshes.Add(PebbleCluster(seed * 31 + 17));             // pebbles
            meshes.Add(ShrubMesh(seed * 31 + 23));                 // cushion shrub

            // Stone colours: a ramp from dark basalt to pale sandstone; each object takes the entry nearest its ground.
            paletteColors = new Color[Palette];
            for (int i = 0; i < Palette; i++)
            {
                float t = i / (Palette - 1f);
                Color c = Color.Lerp(new Color(0.20f, 0.19f, 0.18f), new Color(0.72f, 0.66f, 0.56f), t);
                if (i % 2 == 1) c = new Color(c.r * 1.05f, c.g * 0.98f, c.b * 0.9f); // slightly warmer twin (sandstone, not rust)
                paletteColors[i] = c;
                Material m = ProceduralMeshes.LitMaterial(litShader, c, 0.12f);
                m.enableInstancing = true;
                materials.Add(m);
            }
            Material shrub = ProceduralMeshes.LitMaterial(litShader, new Color(0.16f, 0.24f, 0.09f), 0.05f);
            shrub.enableInstancing = true;
            shrubMaterial = materials.Count;
            materials.Add(shrub);
        }

        void OnDestroy()
        {
            foreach (Mesh m in meshes) if (m != null) Destroy(m);
            foreach (Material m in materials) if (m != null) Destroy(m);
        }

        void LateUpdate()
        {
            if (!enabledForBody || planet == null || planet.Model == null || planet.Sampler == null) return;
            var model = planet.Model;
            double t = simulation.RenderTime;
            Vector3d viewerFixed = model.ToFixed(origin.ViewerPosition, t);
            double r = viewerFixed.Magnitude;
            if (r < 1.0) return;
            Vector3d foot = viewerFixed / r;
            double altitude = r - model.SurfaceRadius(foot);

            if (job != null && job.IsCompleted)
            {
                if (job.IsFaulted) Debug.LogError("[SpaceSim] Surface scatter failed: " + job.Exception, this);
                else Apply(job.Result);
                job = null;
            }
            bool far = altitude > MaxViewerAltitude || planet.RenderScale < 1.0;
            if (far)
            {
                Count = 0;
                return;
            }
            double moved = hasBuild ? (foot - builtFoot).Magnitude * model.Radius : double.MaxValue;
            if (job == null && moved > RebuildDistance)
            {
                PlanetSurfaceSampler sampler = planet.Sampler;
                double radius = model.Radius;
                int seed = planet.Definition.generation.seed;
                Vector3d pad = padDirection, hangar = hangarDirection;
                double padCos = padCosClearance, hangarCos = hangarCosClearance;
                Color[] palette = paletteColors;
                int shrubIndex = shrubMaterial;
                job = Task.Run(() => Build(sampler, radius, seed, foot, pad, padCos, hangar, hangarCos, palette, shrubIndex));
            }
            if (!hasBuild) return;
            Draw(model, t);
        }

        void Apply(Result result)
        {
            anchorFixed = result.Anchor;
            builtFoot = result.Foot;
            hasBuild = true;
            batches.Clear();
            var map = new Dictionary<long, Batch>();
            foreach (Instance inst in result.Instances)
            {
                long key = ((long)inst.Mesh << 32) | ((long)inst.Material << 1) | (inst.Shadow ? 1L : 0L);
                if (!map.TryGetValue(key, out Batch b))
                {
                    b = new Batch { Mesh = meshes[inst.Mesh], Material = materials[inst.Material], Shadow = inst.Shadow, Local = new Matrix4x4[16] };
                    map[key] = b;
                    batches.Add(b);
                }
                if (b.Count == b.Local.Length) Array.Resize(ref b.Local, b.Local.Length * 2);
                b.Local[b.Count++] = inst.Local;
            }
            Count = result.Instances.Count;
            foreach (Batch b in batches) b.World = new Matrix4x4[b.Count];
        }

        void Draw(SpaceSim.Planets.PlanetModel model, double t)
        {
            Vector3 anchorUnity = origin.ToUnityPosition(model.ToInertial(anchorFixed, t));
            Quaternion spin = CoordinateSystem.ToUnityRotation(model.Rotation.Orientation(t));
            Matrix4x4 parent = Matrix4x4.TRS(anchorUnity, spin, Vector3.one);
            int layer = gameObject.layer;
            foreach (Batch b in batches)
            {
                for (int i = 0; i < b.Count; i++) b.World[i] = parent * b.Local[i];
                ShadowCastingMode shadows = b.Shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (b.Count <= MaxPerCall)
                {
                    Graphics.DrawMeshInstanced(b.Mesh, 0, b.Material, b.World, b.Count, null, shadows, true, layer);
                    continue;
                }
                for (int start = 0; start < b.Count; start += MaxPerCall)
                {
                    int n = Math.Min(MaxPerCall, b.Count - start);
                    var part = new Matrix4x4[n];
                    Array.Copy(b.World, start, part, 0, n);
                    Graphics.DrawMeshInstanced(b.Mesh, 0, b.Material, part, n, null, shadows, true, layer);
                }
            }
        }

        /// <summary>Worker thread: decides every cell's content around <paramref name="foot"/> (unit direction, planet-fixed).</summary>
        static Result Build(PlanetSurfaceSampler sampler, double radius, int seed, Vector3d foot, Vector3d pad, double padCos,
            Vector3d hangar, double hangarCos, Color[] palette, int shrubMaterial)
        {
            var list = new List<Instance>(2048);
            double latStep = CellSize / radius;
            double footLat = Math.Asin(MathD.Clamp(foot.z, -1.0, 1.0));
            double footLon = Math.Atan2(foot.y, foot.x);
            double anchorRadius = radius + sampler.SurfaceHeight(foot);
            Vector3d anchor = foot * anchorRadius;
            double cosLimit = Math.Cos(Radius / radius);
            int rows = (int)Math.Ceiling(Radius / CellSize) + 1;
            int i0 = (int)Math.Floor(footLat / latStep);
            ulong baseSeed = DeterministicRandom.Mix((ulong)(uint)seed, 0x5CA77E2UL);

            for (int i = i0 - rows; i <= i0 + rows; i++)
            {
                double latC = (i + 0.5) * latStep;
                if (Math.Abs(latC) >= 0.5 * Math.PI) continue;
                int nLon = Math.Max(1, (int)Math.Floor(2.0 * Math.PI * radius * Math.Cos(latC) / CellSize));
                double lonStep = 2.0 * Math.PI / nLon;
                int jc = (int)Math.Floor((footLon + Math.PI) / lonStep);
                int span = Math.Min(nLon / 2, (int)Math.Ceiling(Radius / (radius * Math.Max(Math.Cos(latC), 1e-6)) / lonStep) + 1);
                for (int dj = -span; dj <= span; dj++)
                {
                    int j = ((jc + dj) % nLon + nLon) % nLon;
                    var rng = new DeterministicRandom(DeterministicRandom.Mix(DeterministicRandom.Mix(baseSeed, (ulong)(long)i), (ulong)j));
                    double lat = (i + rng.NextDouble()) * latStep;
                    double lon = -Math.PI + (j + rng.NextDouble()) * lonStep;
                    Vector3d dir = PlanetRotation.DirectionFromLatLon(lat, lon);
                    if (Vector3d.Dot(dir, foot) < cosLimit) continue;
                    if (Vector3d.Dot(dir, pad) > padCos || Vector3d.Dot(dir, hangar) > hangarCos) continue;
                    double roll = rng.NextDouble();
                    SurfaceSample s = sampler.Sample(dir);
                    if (s.IsWater) continue;

                    float density;
                    switch (s.Biome)
                    {
                        case Biome.Rock: density = 0.75f; break;
                        case Biome.Desert: density = 0.45f; break;
                        case Biome.Tundra: density = 0.4f; break;
                        case Biome.Beach: density = 0.12f; break;
                        case Biome.Snow: density = 0.1f; break;
                        case Biome.Forest: density = 0.3f; break;
                        default: density = 0.22f; break;
                    }
                    // Patchiness: stones gather in fields (a cheap large-scale variation from the cell index).
                    double patch = 0.5 + 0.5 * Math.Sin(i * 0.071 + 1.3 * Math.Sin(j * 0.053)) * Math.Cos(j * 0.067 - 0.9 * Math.Sin(i * 0.049));
                    density *= (float)(0.35 + 1.3 * patch);
                    if (roll > density) continue;

                    bool green = s.Biome == Biome.Grassland || s.Biome == Biome.Forest;
                    double kindRoll = rng.NextDouble();
                    Kind kind = green && kindRoll < 0.45 ? Kind.Shrub
                        : kindRoll < 0.62 ? Kind.Rock
                        : kindRoll < 0.78 ? Kind.Pebbles
                        : Kind.Slab;

                    double size;
                    int mesh;
                    Vector3 shape;
                    switch (kind)
                    {
                        case Kind.Shrub:
                            size = rng.Range(0.35, 1.1);
                            mesh = RockVariants + 2;
                            shape = new Vector3(1f, (float)rng.Range(0.5, 0.8), 1f);
                            break;
                        case Kind.Pebbles:
                            size = rng.Range(0.8, 1.8);
                            mesh = RockVariants + 1;
                            shape = Vector3.one;
                            break;
                        case Kind.Slab:
                            size = rng.Range(1.2, 4.0);
                            mesh = RockVariants;
                            shape = new Vector3(1f, (float)rng.Range(0.6, 1.0), (float)rng.Range(0.6, 1.0));
                            break;
                        default:
                            // Sizes follow a steep power law: many fist-to-knee stones, few boulders, rare house-sized blocks.
                            double u = rng.NextDouble();
                            size = 0.4 * Math.Pow(1.0 - 0.995 * u, -0.6);
                            if (size > 6.0) size = 6.0;
                            mesh = rng.NextInt(RockVariants);
                            shape = new Vector3(1f, (float)rng.Range(0.55, 1.0), (float)rng.Range(0.7, 1.1));
                            break;
                    }

                    double ground = radius + s.SurfaceHeight;
                    // Sink a third of the height: no floating stones on slopes, and they look embedded, not placed.
                    double sink = kind == Kind.Shrub ? 0.08 * size : kind == Kind.Pebbles ? 0.05 * size : 0.33 * size * shape.y;
                    Vector3d pos = dir * (ground - sink);

                    PlanetRotation.LocalBasis(dir, out Vector3d east, out Vector3d north);
                    QuaternionD basis = PlanetFixedObject.BasisToQuaternion(east, north, dir);
                    Quaternion local = CoordinateSystem.ToUnityRotation(basis) *
                        Quaternion.Euler((float)rng.Range(-12.0, 12.0), (float)rng.Range(0.0, 360.0), (float)rng.Range(-12.0, 12.0));
                    Vector3 offset = CoordinateSystem.ToUnityVector(pos - anchor);

                    int material;
                    if (kind == Kind.Shrub) material = shrubMaterial;
                    else
                    {
                        // Stones take the ground's brightness and warmth, a little darker and greyer (no green stones on grass).
                        ColorRGB a = s.Albedo;
                        float lum = 0.2126f * a.r + 0.7152f * a.g + 0.0722f * a.b;
                        float grey = Mathf.Clamp01(lum * 0.9f + 0.08f + (float)rng.Range(-0.08, 0.08));
                        bool warm = a.r > a.g * 1.05f; // ochre/red ground (deserts), not green
                        int idx = Mathf.Clamp(Mathf.RoundToInt(grey * (Palette - 1) / 0.75f), 0, Palette - 1);
                        if (warm && idx % 2 == 0 && idx + 1 < Palette) idx++;
                        else if (!warm && idx % 2 == 1) idx--;
                        material = idx;
                    }
                    list.Add(new Instance
                    {
                        Mesh = mesh,
                        Material = material,
                        Shadow = size > 0.6,
                        Local = Matrix4x4.TRS(offset, local, shape * (float)size)
                    });
                }
            }
            return new Result { Anchor = anchor, Foot = foot, Instances = list };
        }

        // ---- Meshes (unit size, base near y = 0) ----

        /// <summary>A lumpy, faceted stone: an icosphere pushed in and out by value noise, flattened underneath.</summary>
        static Mesh RockMesh(int seed, float roughness, float flatBottom)
        {
            var rng = new DeterministicRandom((ulong)(uint)seed * 2654435761UL + 1UL);
            Icosphere(2, out List<Vector3> v, out List<int> tri);
            var noise = new GradientNoise3D((ulong)(uint)seed);
            var stretch = new Vector3((float)rng.Range(0.8, 1.25), 1f, (float)rng.Range(0.8, 1.25));
            for (int k = 0; k < v.Count; k++)
            {
                Vector3 p = v[k];
                double n = noise.Fbm(new Vector3d(p.x, p.y, p.z) * 1.6, 4, 2.0, 0.5);
                float r = 1f + roughness * (float)n;
                p = Vector3.Scale(p * r, stretch);
                if (p.y < -flatBottom) p.y = -flatBottom + (p.y + flatBottom) * 0.15f;
                v[k] = p * 0.5f;
            }
            return Faceted("Rock", v, tri);
        }

        static Mesh PebbleCluster(int seed)
        {
            var rng = new DeterministicRandom((ulong)(uint)seed * 40503UL + 7UL);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int count = 7;
            for (int c = 0; c < count; c++)
            {
                Icosphere(1, out List<Vector3> v, out List<int> tri);
                float s = (float)rng.Range(0.08, 0.2);
                var centre = new Vector3((float)rng.Range(-0.45, 0.45), s * 0.3f, (float)rng.Range(-0.45, 0.45));
                var squash = new Vector3((float)rng.Range(0.8, 1.3), (float)rng.Range(0.45, 0.75), (float)rng.Range(0.8, 1.3));
                int b = verts.Count;
                foreach (Vector3 p in v) verts.Add(centre + Vector3.Scale(p, squash) * s);
                foreach (int t in tri) tris.Add(b + t);
            }
            return Faceted("Pebbles", verts, tris);
        }

        /// <summary>A low cushion shrub: a rounded, knobbly dome (alpine "cushion plants" — no trunk, nothing tree-like).</summary>
        static Mesh ShrubMesh(int seed)
        {
            Icosphere(2, out List<Vector3> v, out List<int> tri);
            var noise = new GradientNoise3D((ulong)(uint)seed);
            for (int k = 0; k < v.Count; k++)
            {
                Vector3 p = v[k];
                double n = noise.Fbm(new Vector3d(p.x, p.y, p.z) * 4.0, 3, 2.0, 0.5);
                p *= 1f + 0.35f * (float)n;
                if (p.y < -0.1f) p.y = -0.1f;
                v[k] = p * 0.5f;
            }
            return Faceted("Shrub", v, tri);
        }

        /// <summary>Flat-shaded mesh (each triangle its own vertices) — crisp stone facets catch the low sun.</summary>
        static Mesh Faceted(string name, List<Vector3> v, List<int> tri)
        {
            var verts = new Vector3[tri.Count];
            var idx = new int[tri.Count];
            for (int k = 0; k < tri.Count; k++)
            {
                verts[k] = v[tri[k]];
                idx[k] = k;
            }
            var mesh = new Mesh { name = name, vertices = verts, triangles = idx };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Icosphere(int subdivisions, out List<Vector3> v, out List<int> tri)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int k = 0; k < v.Count; k++) v[k] = v[k].normalized;
            tri = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var next = new List<int>(tri.Count * 4);
                for (int k = 0; k < tri.Count; k += 3)
                {
                    int a = tri[k], b = tri[k + 1], c = tri[k + 2];
                    int ab = Mid(v, cache, a, b), bc = Mid(v, cache, b, c), ca = Mid(v, cache, c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tri = next;
            }
        }

        static int Mid(List<Vector3> v, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int m)) return m;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            cache[key] = v.Count - 1;
            return v.Count - 1;
        }
    }
}
