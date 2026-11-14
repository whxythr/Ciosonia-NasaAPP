using System;
using System.Collections.Generic;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Shared materials of procedural rocket parts. One set per owner (flight body, hangar); <see cref="Dispose"/> frees them.</summary>
    public sealed class RocketPalette : IDisposable
    {
        public readonly Material White, Dark, Metal, Niobium, Copper, Gold, Panel, Foam, Grey, Steel, Black, Signal, Accent,
            Shield, ShieldHeavy, Ceramic,
            Insulation, Glass, Rubber, Radiator, Titanium, Ablator, Carbon, Interior, CanopyOrange, CanopyWhite, Lens;
        readonly List<Material> all = new List<Material>();

        public RocketPalette(Shader lit)
        {
            White = Make(lit, new Color(0.90f, 0.90f, 0.91f), 0.35f);
            Dark = Make(lit, new Color(0.13f, 0.13f, 0.14f), 0.3f);
            Metal = Make(lit, new Color(0.38f, 0.36f, 0.34f), 0.5f, 0.6f);
            Niobium = Make(lit, new Color(0.22f, 0.22f, 0.24f), 0.55f, 0.7f);
            Copper = Make(lit, new Color(0.72f, 0.42f, 0.26f), 0.5f, 0.8f);
            Gold = Make(lit, new Color(0.85f, 0.65f, 0.25f), 0.5f, 0.8f);
            Panel = Make(lit, new Color(0.08f, 0.12f, 0.30f), 0.6f);
            Foam = Make(lit, new Color(0.78f, 0.43f, 0.17f), 0.15f);
            Grey = Make(lit, new Color(0.62f, 0.63f, 0.64f), 0.3f);
            Steel = Make(lit, new Color(0.45f, 0.46f, 0.48f), 0.4f, 0.5f);
            Black = Make(lit, new Color(0.03f, 0.03f, 0.035f), 0.6f);
            Signal = Make(lit, new Color(0.95f, 0.78f, 0.12f), 0.3f);
            Accent = Make(lit, new Color(0.25f, 0.65f, 0.95f), 0.4f);
            Shield = Make(lit, new Color(0.30f, 0.33f, 0.40f), 0.35f, 0.5f);
            ShieldHeavy = Make(lit, new Color(0.20f, 0.21f, 0.25f), 0.4f, 0.7f);
            Ceramic = Make(lit, new Color(0.70f, 0.76f, 0.70f), 0.15f);
            // Hydrogen tanks: pale, rough spray-on insulation (not the orange foam of the hydrogen-oxygen stages).
            Insulation = Make(lit, new Color(0.86f, 0.84f, 0.77f), 0.08f);
            Glass = Make(lit, new Color(0.04f, 0.07f, 0.11f), 0.92f, 0.3f);
            Rubber = Make(lit, new Color(0.07f, 0.07f, 0.07f), 0.12f);
            Radiator = Make(lit, new Color(0.80f, 0.82f, 0.84f), 0.45f, 0.3f);
            Titanium = Make(lit, new Color(0.56f, 0.57f, 0.60f), 0.55f, 0.85f);
            Ablator = Make(lit, new Color(0.30f, 0.20f, 0.12f), 0.08f);
            Carbon = Make(lit, new Color(0.10f, 0.10f, 0.11f), 0.55f, 0.2f);
            Interior = Make(lit, new Color(0.36f, 0.38f, 0.41f), 0.2f, 0.2f);
            CanopyOrange = Make(lit, new Color(0.95f, 0.40f, 0.10f), 0.2f);
            CanopyWhite = Make(lit, new Color(0.94f, 0.94f, 0.92f), 0.2f);
            Lens = Make(lit, new Color(0.92f, 0.93f, 0.86f), 0.9f);
            Lens.EnableKeyword("_EMISSION");
            if (Lens.HasProperty("_EmissionColor")) Lens.SetColor("_EmissionColor", new Color(0.55f, 0.55f, 0.5f));
        }

        Material Make(Shader shader, Color color, float smoothness, float metallic = 0f)
        {
            Material m = ProceduralMeshes.LitMaterial(shader, color, smoothness, metallic);
            all.Add(m);
            return m;
        }

        public void Dispose()
        {
            foreach (Material m in all)
                if (m != null) UnityEngine.Object.Destroy(m);
            all.Clear();
        }
    }

    /// <summary>
    /// A built rocket: one GameObject per stage (hidden when it separates), the payload section, the fairing, and one slot per
    /// placed part with its renderers (for highlighting and picking in the hangar). Positions follow <see cref="Layout"/>:
    /// <see cref="Root"/>'s y = 0 is the bottom of stage 1.
    /// </summary>
    public sealed class RocketModel
    {
        public sealed class Part
        {
            public PartPlacement Placement;
            /// <summary>Slot transform: bottom centre of the slot, +Z pointing away from the axis.</summary>
            public Transform Transform;
            public Renderer[] Renderers;
            /// <summary>Drawn with a model from <see cref="ModuleVisualLibrary"/> (false = procedural placeholder).</summary>
            public bool FromLibrary;
        }

        internal sealed class LegRig
        {
            public Transform Hinge, Foot;
            public float Current, Target;
        }

        internal sealed class CanopyRig
        {
            public GameObject Root;
            public Transform Inflate;
        }

        public Transform Root;
        public RocketLayout Layout;
        public GameObject[] Stages = new GameObject[0];
        public GameObject Payload;
        public GameObject Fairing;
        public readonly List<Part> Parts = new List<Part>();
        internal readonly List<GameObject> StowedWings = new List<GameObject>(), DeployedWings = new List<GameObject>();
        internal readonly List<Mesh> OwnedMeshes = new List<Mesh>();
        internal readonly List<LegRig> Legs = new List<LegRig>();
        internal readonly List<CanopyRig> Canopies = new List<CanopyRig>();
        RocketModelAnimator animator;

        /// <summary>Mount points of the installed lamps (+Z is the beam direction).</summary>
        public readonly List<Transform> LightAnchors = new List<Transform>();
        /// <summary>
        /// Wheels of the vehicle (local X is the axle, the origin is the wheel centre, localRotation is identity at rest): the rover
        /// controller spins and steers them.
        /// </summary>
        public readonly List<Transform> WheelTransforms = new List<Transform>();

        public bool LegsDeployed { get; private set; }
        public float ParachuteOpening { get; private set; }

        /// <summary>Shows the canopies of the installed parachutes; <paramref name="opening"/> is how far they have inflated, 0..1.</summary>
        public void SetParachutesDeployed(bool deployed, float opening = 1f)
        {
            float o = deployed ? Mathf.Clamp01(opening) : 0f;
            ParachuteOpening = o;
            bool show = deployed && o > 0.001f;
            // A long streamer first, then the canopy fills: the width grows later than the length.
            float across = Mathf.Lerp(0.05f, 1f, o * o), along = Mathf.Lerp(0.3f, 1f, Mathf.Sqrt(o));
            foreach (CanopyRig c in Canopies)
            {
                if (c == null || c.Root == null) continue;
                if (c.Root.activeSelf != show) c.Root.SetActive(show);
                if (show && c.Inflate != null) c.Inflate.localScale = new Vector3(across, along, across);
            }
        }

        /// <summary>Folds or extends the landing legs (they swing out smoothly over about 1.5 s).</summary>
        public void SetLegsDeployed(bool deployed)
        {
            LegsDeployed = deployed;
            float target = deployed ? 1f : 0f;
            bool moving = false;
            foreach (LegRig l in Legs)
            {
                l.Target = target;
                moving |= l.Current != target;
            }
            if (!moving || Root == null) return;
            if (!Root.gameObject.activeInHierarchy)
            {
                StepLegs(1e6f); // nobody sees the motion: snap
                return;
            }
            if (animator == null) animator = Root.gameObject.AddComponent<RocketModelAnimator>();
            animator.Model = this;
            animator.enabled = true;
        }

        const float LegSeconds = 1.5f;

        /// <summary>Moves the legs towards their target; true while any still moves.</summary>
        internal bool StepLegs(float dt)
        {
            bool moving = false;
            float step = Mathf.Max(0f, dt) / LegSeconds;
            foreach (LegRig l in Legs)
            {
                float delta = l.Target - l.Current;
                if (Mathf.Abs(delta) <= step) l.Current = l.Target;
                else
                {
                    l.Current += delta > 0f ? step : -step;
                    moving = true;
                }
                ApplyLeg(l);
            }
            return moving;
        }

        internal static void ApplyLeg(LegRig l)
        {
            if (l.Hinge == null) return;
            float a = Mathf.SmoothStep(0f, 1f, l.Current) * (float)RocketLayout.LegDeployAngle;
            l.Hinge.localRotation = Quaternion.Euler(a, 0f, 0f); // +Y (along the stowed leg) swings out (+Z) and down
            if (l.Foot != null) l.Foot.localRotation = Quaternion.Euler(-a, 0f, 0f); // the pad stays level
        }

        /// <summary>How many parts use a real model from the library.</summary>
        public int LibraryModels
        {
            get
            {
                int n = 0;
                foreach (Part p in Parts)
                    if (p.FromLibrary) n++;
                return n;
            }
        }

        /// <summary>
        /// Solar wings, big dishes and radiators: folded against the hull under the fairing, open once the vehicle flies free.
        /// </summary>
        public void SetWingsDeployed(bool deployed)
        {
            foreach (GameObject g in StowedWings)
                if (g != null) g.SetActive(!deployed);
            foreach (GameObject g in DeployedWings)
                if (g != null) g.SetActive(deployed);
        }

        public Part Find(PartPlacement placement)
        {
            foreach (Part p in Parts)
                if (p.Placement == placement) return p;
            return null;
        }

        static MaterialPropertyBlock block;

        /// <summary>Tints a part towards <paramref name="color"/> (amount 0 = its own colours). Visual feedback only.</summary>
        public static void Tint(Part part, Color color, float amount)
        {
            if (part == null || part.Renderers == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (Renderer r in part.Renderers)
            {
                if (r == null) continue;
                if (amount <= 0f)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                Material m = r.sharedMaterial;
                Color own = Color.white;
                if (m != null && m.HasProperty("_BaseColor")) own = m.GetColor("_BaseColor");
                else if (m != null && m.HasProperty("_Color")) own = m.GetColor("_Color");
                Color c = Color.Lerp(own, color, Mathf.Clamp01(amount));
                block.Clear();
                block.SetColor("_BaseColor", c);
                block.SetColor("_Color", c);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>Destroys the GameObjects and the meshes made for this rocket (shared meshes and materials stay).</summary>
        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            foreach (Mesh m in OwnedMeshes)
                if (m != null) UnityEngine.Object.Destroy(m);
            OwnedMeshes.Clear();
            Parts.Clear();
            Legs.Clear();
            Canopies.Clear();
            LightAnchors.Clear();
            WheelTransforms.Clear();
            StowedWings.Clear();
            DeployedWings.Clear();
            animator = null;
            Root = null;
        }
    }

    /// <summary>
    /// Builds the 3D rocket from a design: <see cref="RocketLayout"/> (Core) says where each part goes; each module is drawn with
    /// its model from <see cref="ModuleVisualLibrary"/> when one is assigned, otherwise with a procedural placeholder of the same
    /// size. Used by the flying rocket, by the hangar preview and by the catalog thumbnails, so all of them always look the same.
    /// Purely visual.
    /// </summary>
    public static class RocketModelBuilder
    {
        public static RocketModel Build(RocketBlueprint design, double radius, Func<string, bool> isUnlocked, Transform parent,
            RocketPalette palette, ModuleVisualLibrary library)
        {
            RocketLayout layout = RocketLayout.Build(design ?? RocketBlueprint.CreateDefault(), radius, isUnlocked);
            var model = new RocketModel { Layout = layout };
            model.Root = new GameObject("Rocket").transform;
            model.Root.SetParent(parent, false);
            model.Stages = new GameObject[layout.StageCount];
            for (int i = 0; i < layout.StageCount; i++)
            {
                model.Stages[i] = new GameObject("Stage" + (i + 1));
                model.Stages[i].transform.SetParent(model.Root, false);
            }
            model.Payload = new GameObject("Payload");
            model.Payload.transform.SetParent(model.Root, false);
            model.Fairing = new GameObject("Fairing");
            model.Fairing.transform.SetParent(model.Root, false);

            foreach (PartPlacement p in layout.Parts)
            {
                Transform group = p.Role == PartRole.Fairing ? model.Fairing.transform
                    : p.Stage >= 0 && p.Stage < model.Stages.Length ? model.Stages[p.Stage].transform
                    : model.Payload.transform;
                model.Parts.Add(BuildSlot(p, group, palette, library, model));
            }
            return model;
        }

        /// <summary>
        /// One module on its own (catalog thumbnails): its slot from <see cref="RocketLayout.PreviewSlot"/> at the origin, drawn
        /// exactly as on a rocket. Null for an unknown id. Destroy it with <see cref="RocketModel.Destroy"/>.
        /// </summary>
        public static RocketModel BuildPreview(string moduleId, Transform parent, RocketPalette palette, ModuleVisualLibrary library)
        {
            ModuleDefinition m = ModuleCatalog.Find(moduleId);
            if (m == null) return null;
            PartPlacement p = RocketLayout.PreviewSlot(m);
            var model = new RocketModel();
            model.Root = new GameObject("Preview " + moduleId).transform;
            model.Root.SetParent(parent, false);
            model.Payload = model.Root.gameObject;
            model.Parts.Add(BuildSlot(p, model.Root, palette, library, model));
            model.SetWingsDeployed(false);
            return model;
        }

        static RocketModel.Part BuildSlot(PartPlacement p, Transform group, RocketPalette palette, ModuleVisualLibrary library, RocketModel model)
        {
            var slot = new GameObject(SlotName(p)).transform;
            slot.SetParent(group, false);
            slot.localPosition = new Vector3((float)p.X, (float)p.Bottom, (float)p.Z);
            slot.localRotation = Quaternion.Euler(0f, (float)p.AngleDegrees, 0f);

            ModuleDefinition module = p.IsModule ? ModuleCatalog.Find(p.ModuleId) : null;
            ModuleVisualLibrary.Entry entry = library != null && p.IsModule ? library.Find(p.ModuleId) : null;
            bool fromLibrary = entry != null && FromLibrary(entry, p, module, slot, palette, model);
            if (!fromLibrary) Placeholder(p, module, slot, palette, model);
            return new RocketModel.Part
            {
                Placement = p, Transform = slot, Renderers = slot.GetComponentsInChildren<Renderer>(), FromLibrary = fromLibrary
            };
        }

        static string SlotName(PartPlacement p)
        {
            string where = p.Stage >= 0 ? "S" + (p.Stage + 1) + " " : "";
            string index = p.Index >= 0 ? " " + (p.Index + 1) : "";
            string piece = p.IsStageSide ? "." + (p.Element + 1) : "";
            return where + p.Role + index + piece + (p.IsModule ? " · " + p.ModuleId : "");
        }

        // ---- Models from the library ------------------------------------------------------------------------

        /// <summary>
        /// A library model in its slot, with the moving parts the rest of the game relies on: a leg turns about its hinge, a wheel
        /// spins about its axle, a lamp gets its beam anchor and a parachute its (procedural) canopy.
        /// </summary>
        static bool FromLibrary(ModuleVisualLibrary.Entry entry, PartPlacement p, ModuleDefinition m, Transform slot, RocketPalette c, RocketModel model)
        {
            float d = (float)p.Diameter, h = (float)p.Height;
            switch (p.Role)
            {
                case PartRole.LandingLeg:
                {
                    var hinge = new GameObject("Hinge").transform;
                    hinge.SetParent(slot, false);
                    if (!InstantiateModel(entry, hinge, d, h, false))
                    {
                        UnityEngine.Object.Destroy(hinge.gameObject);
                        return false;
                    }
                    model.Legs.Add(new RocketModel.LegRig { Hinge = hinge });
                    return true;
                }
                case PartRole.Wheel:
                {
                    Transform wheel = WheelFrame(slot, p, model);
                    if (!InstantiateModel(entry, wheel, d, h, true))
                    {
                        model.WheelTransforms.Remove(wheel);
                        UnityEngine.Object.Destroy(wheel.parent.gameObject);
                        return false;
                    }
                    return true;
                }
            }
            if (!InstantiateModel(entry, slot, d, h, false)) return false;
            if (m != null && m.auxiliaryKind == AuxiliaryKind.Light) Anchor(slot, model, new Vector3(0f, 0.5f * h, 0.5f * d));
            if (p.Role == PartRole.Parachute) Canopy(slot, c, m, p, model);
            return true;
        }

        /// <summary>
        /// Instantiates the assigned model under a fitting node in <paramref name="frame"/>: the model is rotated, measured in that
        /// frame, then scaled (uniformly or per axis) to the box <paramref name="d"/> × <paramref name="h"/> × <paramref name="d"/>
        /// and shifted so its bounds stand on the frame origin (or are centred on it).
        /// </summary>
        static bool InstantiateModel(ModuleVisualLibrary.Entry entry, Transform frame, float d, float h, bool centred)
        {
            if (entry == null || entry.model == null) return false;
            var fit = new GameObject("Fit").transform;
            fit.SetParent(frame, false);
            GameObject go = UnityEngine.Object.Instantiate(entry.model, fit);
            if (go == null)
            {
                UnityEngine.Object.Destroy(fit.gameObject);
                return false;
            }
            go.name = entry.model.name;
            foreach (Collider col in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(col); // flight never uses Unity physics
            Transform t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Euler(entry.rotation) * t.localRotation;

            float scale = entry.scale > 0f ? entry.scale : 1f;
            if (!LocalBounds(go, frame, out Vector3 min, out Vector3 max))
            {
                fit.localScale = Vector3.one * scale;
                fit.localPosition = entry.offset;
                return true;
            }
            Vector3 size = max - min;
            Vector3 k = Vector3.one;
            switch (entry.fit)
            {
                case ModuleVisualLibrary.FitMode.FitInside:
                {
                    float u = float.MaxValue;
                    if (size.x > 1e-4f) u = Mathf.Min(u, d / size.x);
                    if (size.y > 1e-4f) u = Mathf.Min(u, h / size.y);
                    if (size.z > 1e-4f) u = Mathf.Min(u, d / size.z);
                    if (u == float.MaxValue) u = 1f;
                    k = new Vector3(u, u, u);
                    break;
                }
                case ModuleVisualLibrary.FitMode.Stretch:
                    k = new Vector3(size.x > 1e-4f ? d / size.x : 1f, size.y > 1e-4f ? h / size.y : 1f, size.z > 1e-4f ? d / size.z : 1f);
                    break;
            }
            k = k * scale;
            Vector3 centre = (min + max) * 0.5f;
            fit.localScale = k;
            float y = centred ? -centre.y * k.y : -min.y * k.y;
            fit.localPosition = new Vector3(-centre.x * k.x, y, -centre.z * k.z) + entry.offset;
            return true;
        }

        /// <summary>Axis-aligned bounds of everything rendered under <paramref name="go"/>, in <paramref name="frame"/>'s local space.</summary>
        static bool LocalBounds(GameObject go, Transform frame, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;
            foreach (MeshFilter f in go.GetComponentsInChildren<MeshFilter>())
            {
                if (f.sharedMesh == null) continue;
                Bounds b = f.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 local = frame.InverseTransformPoint(f.transform.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    any = true;
                }
            }
            foreach (SkinnedMeshRenderer s in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Bounds b = s.localBounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 local = frame.InverseTransformPoint(s.transform.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    any = true;
                }
            }
            return any;
        }

        // ---- Shared meshes ----------------------------------------------------------------------------------

        static Mesh bell, nozzleExtension, ogive, dish, tube, octagon, hexagon, dome, canopy, torusThin, torusFat, cone;
        static Mesh Bell => bell != null ? bell : bell = ProceduralMeshes.Lathe(28, Profile(10, s => new Vector2(0.16f + 0.34f * Mathf.Sqrt(1f - s), s)), true, "Bell");
        static Mesh NozzleExtension => nozzleExtension != null ? nozzleExtension
            : nozzleExtension = ProceduralMeshes.Lathe(28, Profile(8, s => new Vector2(0.5f - 0.18f * s * s, s)), true, "NozzleExtension");
        /// <summary>Tangent ogive of fineness 1.5 (unit base radius, unit height): scale x/z by the radius, y by 1.5 × radius.</summary>
        static Mesh Ogive => ogive != null ? ogive : ogive = ProceduralMeshes.Lathe(40, Profile(18, s =>
        {
            const double L = 1.5, rho = 1.625; // (R² + L²) / 2R for R = 1
            double y = s * L;
            return new Vector2((float)Math.Max(0.0, Math.Sqrt(rho * rho - y * y) + 1.0 - rho), s);
        }), false, "Ogive");
        /// <summary>Paraboloid y = (2r)², rim radius 0.5 at y = 1, open towards +Y, both sides drawn.</summary>
        static Mesh Dish => dish != null ? dish : dish = ProceduralMeshes.Lathe(32, Profile(8, s => new Vector2(0.5f * s, s * s)), true, "Dish");
        static Mesh Tube => tube != null ? tube : tube = ProceduralMeshes.Lathe(40, new[] { new Vector2(0.5f, 0f), new Vector2(0.5f, 1f) }, true, "Tube");
        static Mesh Octagon => octagon != null ? octagon : octagon = ProceduralMeshes.Prism(8, "Octagon");
        static Mesh Hexagon => hexagon != null ? hexagon : hexagon = ProceduralMeshes.Prism(6, "Hexagon");
        /// <summary>Half sphere, radius 0.5, base at y = 0.</summary>
        static Mesh Dome => dome != null ? dome : dome = ProceduralMeshes.Lathe(28, Profile(9, s => new Vector2(0.5f * Mathf.Cos(0.5f * Mathf.PI * s), 0.5f * Mathf.Sin(0.5f * Mathf.PI * s))), false, "Dome");
        /// <summary>Parachute canopy: skirt radius 0.5 at y = 0, crown at y = 0.42 with a vent; both sides drawn.</summary>
        static Mesh CanopyMesh => canopy != null ? canopy : canopy = ProceduralMeshes.Lathe(32, Profile(10, s =>
            new Vector2(0.5f * Mathf.Cos(0.46f * Mathf.PI * s), 0.42f * Mathf.Sin(0.5f * Mathf.PI * s))), true, "Canopy");
        /// <summary>Ring around +Y, outer diameter 1, centred on y = 0.</summary>
        static Mesh TorusThin => torusThin != null ? torusThin : torusThin = Torus(0.44f, 0.06f, "TorusThin");
        static Mesh TorusFat => torusFat != null ? torusFat : torusFat = Torus(0.38f, 0.12f, "TorusFat");
        static Mesh Cone => cone != null ? cone : cone = ProceduralMeshes.Frustum(28, 0.5f, 0f, true, false, "Cone");

        static Mesh Torus(float major, float minor, string name)
        {
            const int n = 14;
            var p = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = -Mathf.PI + 2f * Mathf.PI * i / n;
                p[i] = new Vector2(major + minor * Mathf.Cos(a), minor * Mathf.Sin(a));
            }
            return ProceduralMeshes.Lathe(32, p, false, name);
        }

        static Vector2[] Profile(int points, Func<float, Vector2> f)
        {
            var p = new Vector2[points];
            for (int i = 0; i < points; i++) p[i] = f(i / (float)(points - 1));
            return p;
        }

        /// <summary>
        /// Closed solid from 8 corners: two loops of four (same order). Flat-shaded; every face is wound to face away from the centre.
        /// </summary>
        static Mesh Solid(Vector3[] c, string name)
        {
            int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            Vector3 centroid = Vector3.zero;
            foreach (Vector3 v in c) centroid = centroid + v;
            centroid = centroid / 8f;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            foreach (int[] f in faces)
            {
                Vector3 a = c[f[0]], b = c[f[1]], cc = c[f[2]], d = c[f[3]];
                Vector3 n = Vector3.Cross(b - a, cc - a).normalized;
                Vector3 mid = (a + b + cc + d) * 0.25f;
                if (Vector3.Dot(n, mid - centroid) < 0f)
                {
                    Vector3 t = b;
                    b = d;
                    d = t;
                    n = -n;
                }
                int i0 = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(cc); verts.Add(d);
                for (int k = 0; k < 4; k++) normals.Add(n);
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts.ToArray();
            mesh.normals = normals.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Part of a cylinder wall (radius 0.5, y = 0…1) between azimuths <paramref name="from"/> and <paramref name="to"/> [deg]
        /// (0° = +Z, 90° = +X), facing outward or inward.
        /// </summary>
        static Mesh ArcShell(float from, float to, bool outward, string name)
        {
            int seg = Mathf.Max(2, Mathf.RoundToInt((to - from) / 8f));
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = (from + (to - from) * i / seg) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(dir * 0.5f);
                v.Add(dir * 0.5f + Vector3.up);
                n.Add(outward ? dir : -dir);
                n.Add(outward ? dir : -dir);
                uv.Add(new Vector2((float)i / seg, 0f));
                uv.Add(new Vector2((float)i / seg, 1f));
            }
            // Winding: check the first quad against the wanted normal, wind all the same way.
            Vector3 cross = Vector3.Cross(v[1] - v[0], v[2] - v[0]);
            bool keep = Vector3.Dot(cross, n[0]) > 0f;
            for (int i = 0; i < seg; i++)
            {
                int a = 2 * i, b = a + 1, c = a + 2, d = a + 3;
                if (keep)
                {
                    t.Add(a); t.Add(b); t.Add(c);
                    t.Add(c); t.Add(b); t.Add(d);
                }
                else
                {
                    t.Add(a); t.Add(c); t.Add(b);
                    t.Add(c); t.Add(d); t.Add(b);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = v.ToArray();
            mesh.normals = n.ToArray();
            mesh.uv = uv.ToArray();
            mesh.triangles = t.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- Primitive helpers (slot frame: origin = bottom centre, +Y up, +Z outward) ------------------------

        static GameObject Cylinder(Transform parent, Material m, float diameter, float height, float bottom, string name = "Cylinder",
            float x = 0f, float z = 0f)
        {
            GameObject g = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, name, parent, m);
            g.transform.localScale = new Vector3(diameter, 0.5f * height, diameter);
            g.transform.localPosition = new Vector3(x, bottom + 0.5f * height, z);
            return g;
        }

        /// <summary>A cylinder of <paramref name="length"/> along <paramref name="axis"/>, centred on <paramref name="centre"/>.</summary>
        static GameObject AxisCylinder(Transform parent, Material m, float diameter, float length, Vector3 centre, Vector3 axis, string name = "Cylinder")
        {
            GameObject g = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, name, parent, m);
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            g.transform.localScale = new Vector3(diameter, 0.5f * length, diameter);
            g.transform.localPosition = centre;
            return g;
        }

        static GameObject Box(Transform parent, Material m, Vector3 size, Vector3 centre, string name = "Box")
        {
            GameObject g = ProceduralMeshes.Primitive(PrimitiveType.Cube, name, parent, m);
            g.transform.localScale = size;
            g.transform.localPosition = centre;
            return g;
        }

        /// <summary>A square bar from <paramref name="a"/> to <paramref name="b"/>.</summary>
        static GameObject Rod(Transform parent, Material m, Vector3 a, Vector3 b, float thickness, string name = "Rod")
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            GameObject g = ProceduralMeshes.Primitive(PrimitiveType.Cube, name, parent, m);
            g.transform.localPosition = (a + b) * 0.5f;
            g.transform.localRotation = len > 1e-5f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
            g.transform.localScale = new Vector3(thickness, Mathf.Max(1e-4f, len), thickness);
            return g;
        }

        static GameObject Ball(Transform parent, Material m, float diameter, Vector3 centre, string name = "Ball")
        {
            GameObject g = ProceduralMeshes.Primitive(PrimitiveType.Sphere, name, parent, m);
            g.transform.localScale = Vector3.one * diameter;
            g.transform.localPosition = centre;
            return g;
        }

        static GameObject MeshPart(Transform parent, Mesh mesh, Material m, Vector3 scale, Vector3 position, string name)
        {
            GameObject g = ProceduralMeshes.MeshObject(mesh, name, parent, m);
            g.transform.localScale = scale;
            g.transform.localPosition = position;
            return g;
        }

        static GameObject Ring(Transform parent, Material m, float diameter, float height, float bottom, string name = "Ring") =>
            MeshPart(parent, Tube, m, new Vector3(diameter, height, diameter), new Vector3(0f, bottom, 0f), name);

        /// <summary>A nozzle cone whose exit points along <paramref name="direction"/>, its throat at <paramref name="throat"/>.</summary>
        static GameObject Nozzle(Transform parent, Material m, Vector3 throat, Vector3 direction, float exit, float length, string name = "Nozzle")
        {
            Vector3 dir = direction.normalized;
            GameObject g = MeshPart(parent, Bell, m, new Vector3(exit, length, exit), throat + dir * length, name);
            g.transform.localRotation = Quaternion.FromToRotation(new Vector3(0f, -1f, 0f), dir);
            return g;
        }

        static GameObject Group(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = position;
            g.transform.localRotation = rotation;
            return g;
        }

        static void Anchor(Transform parent, RocketModel model, Vector3 position, float tiltDown = 15f)
        {
            GameObject a = Group(parent, "LightAnchor", position, Quaternion.Euler(tiltDown, 0f, 0f));
            model.LightAnchors.Add(a.transform);
        }

        // ---- Placeholders ---------------------------------------------------------------------------------------

        static void Placeholder(PartPlacement p, ModuleDefinition m, Transform slot, RocketPalette c, RocketModel model)
        {
            float d = (float)p.Diameter, h = (float)p.Height;
            RocketLayout layout = model.Layout;
            float r = (float)(layout != null ? layout.Radius : RocketLayout.DefaultRadius);
            switch (p.Role)
            {
                case PartRole.EngineSection:
                {
                    ModuleDefinition engine = StageEngine(layout, p.Stage);
                    bool liquid = engine == null || engine.engineKind == EngineKind.LiquidChemical;
                    if (p.Stage == 0 && liquid) Skirt(slot, c, r, h, !HasSideFins(layout, 0));
                    else Truss(slot, c, r, h);
                    break;
                }
                case PartRole.Engine:
                    switch (m != null ? m.engineKind : EngineKind.LiquidChemical)
                    {
                        case EngineKind.Solid: SolidMotor(slot, c, m, d, h, model); break;
                        case EngineKind.Nuclear: NuclearEngine(slot, c, m, d, h, model); break;
                        case EngineKind.Electric: ElectricThruster(slot, c, m, d, h, model); break;
                        default: Engine(slot, c, m, d, h); break;
                    }
                    break;
                case PartRole.Tank:
                    if (m != null && m.propellant == PropellantType.LiquidHydrogen) HydrogenTank(slot, c, m, d, h);
                    else if (m != null && m.propellant == PropellantType.Xenon) XenonTank(slot, c, m, d, h);
                    else Tank(slot, c, m, d, h);
                    break;
                case PartRole.StageSpacer:
                    MeshPart(slot, Tube, c.Dark, new Vector3(2f * r, h, 2f * r), Vector3.zero, "Spacer");
                    Cylinder(slot, c.Metal, 1.96f * r, 0.08f, Mathf.Max(0f, h - 0.08f), "Deck");
                    break;
                case PartRole.PayloadAdapter:
                {
                    float top = layout != null ? Mathf.Min(r, 0.5f * (float)layout.AdapterTopDiameter) : 0.5f * r;
                    Mesh adapterCone = ProceduralMeshes.Frustum(40, r, top, true, true, "Adapter");
                    model.OwnedMeshes.Add(adapterCone);
                    MeshPart(slot, adapterCone, c.Dark, new Vector3(1f, h, 1f), Vector3.zero, "Adapter");
                    break;
                }
                case PartRole.ProbeBus:
                    if (m == null || m.id == ModuleCatalog.ProbeCore) Bus(slot, c, d, h, model);
                    else SmallProbe(slot, c, d, h);
                    break;
                case PartRole.Capsule: Capsule(slot, c, m, d, h, model); break;
                case PartRole.Wrap: Wrap(slot, c, m, d, h, (float)p.Thickness); break;
                case PartRole.Instrument: Instrument(slot, c, m, d, model); break;
                case PartRole.Fairing: if (layout != null) Fairing(slot, c, layout, model); break;
                case PartRole.SideFin: TailFin(slot, c, m, d, h, model); break;
                case PartRole.GridFin: GridFin(slot, c, m, d, h); break;
                case PartRole.RcsBlock: RcsBlock(slot, c, m, d, h); break;
                case PartRole.LandingLeg: Leg(slot, c, m, d, h, model); break;
                case PartRole.HeatShield: HeatShield(slot, c, m, d, h, model); break;
                case PartRole.Parachute: ParachuteContainer(slot, c, m, d, h); Canopy(slot, c, m, p, model); break;
                case PartRole.CargoBay: CargoBay(slot, c, m, d, h, model); break;
                case PartRole.RoverChassis: Chassis(slot, c, p, d, h, layout); break;
                case PartRole.RoverDrive: Drive(slot, c, d, h); break;
                case PartRole.Wheel: Wheel(WheelFrame(slot, p, model), c, m, d); break;
            }
        }

        static ModuleDefinition StageEngine(RocketLayout layout, int stage)
        {
            if (layout == null) return null;
            foreach (PartPlacement q in layout.Parts)
                if (q.Role == PartRole.Engine && q.Stage == stage) return ModuleCatalog.Find(q.ModuleId);
            return null;
        }

        static bool HasSideFins(RocketLayout layout, int stage)
        {
            if (layout == null) return false;
            foreach (PartPlacement q in layout.Parts)
                if (q.Role == PartRole.SideFin && q.Stage == stage) return true;
            return false;
        }

        // ---- Stage structure ------------------------------------------------------------------------------------

        /// <summary>Stage 1 thrust skirt: the engines hang out of its open bottom; four fins unless the stage has its own fin set.</summary>
        static void Skirt(Transform slot, RocketPalette c, float r, float h, bool fins)
        {
            float skirtBottom = (float)RocketLayout.SkirtOpening * h;
            MeshPart(slot, Tube, c.Dark, new Vector3(2.01f * r, h - skirtBottom, 2.01f * r), new Vector3(0f, skirtBottom, 0f), "Skirt");
            Cylinder(slot, c.Metal, 1.98f * r, 0.08f, skirtBottom, "HeatShield");
            if (!fins) return;
            for (int f = 0; f < 4; f++)
            {
                Quaternion around = Quaternion.Euler(0f, 45f + 90f * f, 0f);
                // Root on the skirt wall, small enough that the engines stay visible between the fins.
                GameObject fin = Box(slot, c.Dark, new Vector3(0.1f, 2.4f, 0.9f * r), around * new Vector3(0f, 1.3f, 1.4f * r), "Fin" + f);
                fin.transform.localRotation = around;
            }
        }

        /// <summary>Upper-stage interstage (and the frame around solid, nuclear and electric engines): rings and struts, open.</summary>
        static void Truss(Transform slot, RocketPalette c, float r, float h)
        {
            MeshPart(slot, Tube, c.Dark, new Vector3(2.01f * r, Mathf.Min(0.2f, 0.3f * h), 2.01f * r), Vector3.zero, "LowerRing");
            float ring = Mathf.Min(0.3f, 0.25f * h);
            MeshPart(slot, Tube, c.Dark, new Vector3(2.01f * r, ring, 2.01f * r), new Vector3(0f, h - ring, 0f), "UpperRing");
            Cylinder(slot, c.Metal, 1.96f * r, 0.06f, h - ring, "ThrustPlate");
            float low = Mathf.Min(0.2f, 0.3f * h);
            for (int k = 0; k < 8; k++)
            {
                Quaternion around = Quaternion.Euler(0f, 22.5f + 45f * k, 0f);
                Box(slot, c.Steel, new Vector3(0.1f, Mathf.Max(0.05f, h - low - ring), 0.1f), around * new Vector3(0f, low + 0.5f * (h - low - ring), 0.97f * r),
                    "Strut" + k);
            }
        }

        // ---- Engines ------------------------------------------------------------------------------------------

        static void Engine(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h)
        {
            bool vacuum = m != null && m.vacuumIsp > 0.0 && m.seaLevelIsp / m.vacuumIsp < 0.75;
            Material bellMaterial = m != null && m.propellant == PropellantType.Hydrolox ? c.Copper
                : m != null && m.propellant == PropellantType.Hypergolic ? c.Grey : c.Metal;
            float bellHeight = h * (vacuum ? 0.72f : 0.6f);
            if (vacuum)
            {
                // Regeneratively cooled bell on top, the dark radiatively cooled extension below it.
                float ext = 0.55f * bellHeight;
                MeshPart(slot, NozzleExtension, c.Niobium, new Vector3(d, ext, d), Vector3.zero, "NozzleExtension");
                float upper = d * (0.5f - 0.18f) * 2f;
                MeshPart(slot, Bell, bellMaterial, new Vector3(upper, bellHeight - ext, upper), new Vector3(0f, ext, 0f), "Bell");
            }
            else MeshPart(slot, Bell, bellMaterial, new Vector3(d, bellHeight, d), Vector3.zero, "Bell");
            float chamber = Mathf.Max(0.12f, 0.34f * d);
            float chamberHeight = 0.22f * h;
            Cylinder(slot, c.Steel, chamber, chamberHeight, bellHeight - 0.02f * h, "Chamber");
            Cylinder(slot, c.Dark, 0.55f * chamber, 0.4f * chamberHeight, bellHeight + 0.2f * chamberHeight, "Turbopump", 0.75f * chamber, 0f);
            float mount = bellHeight + chamberHeight - 0.02f * h;
            Cylinder(slot, c.Dark, 0.5f * chamber, Mathf.Max(0.02f, h - mount), mount, "Gimbal");
            if (m != null && m.requiresPurchase) MeshPart(slot, Tube, c.Accent, new Vector3(1.01f * chamber, 0.06f * h, 1.01f * chamber),
                new Vector3(0f, bellHeight + 0.4f * chamberHeight, 0f), "UpgradeStripe");
        }

        /// <summary>Solid motor: segmented case with field joints, aft skirt and a submerged nozzle; the grain fills the case.</summary>
        static void SolidMotor(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h, RocketModel model)
        {
            string id = m != null ? m.id : "";
            bool small = id == ModuleCatalog.EngineSolidSmall, large = id == ModuleCatalog.EngineSolidLarge;
            Material shell = small ? c.Steel : c.White;
            float nozzle = Mathf.Min(0.14f * h, 1.1f * d);
            float caseD = 0.92f * d;
            MeshPart(slot, Bell, c.Niobium, new Vector3(0.78f * d, nozzle, 0.78f * d), Vector3.zero, "Nozzle");
            Mesh skirt = ProceduralMeshes.Frustum(32, 0.5f * d, 0.5f * caseD, false, false, "AftSkirt");
            model.OwnedMeshes.Add(skirt);
            MeshPart(slot, skirt, large ? c.Dark : c.Grey, new Vector3(1f, 0.9f * nozzle, 1f), new Vector3(0f, 0.35f * nozzle, 0f), "AftSkirt");
            float caseBottom = 1.2f * nozzle, domeH = 0.3f * caseD;
            float caseTop = Mathf.Max(caseBottom + 0.1f, h - domeH - 0.05f * h);
            Cylinder(slot, shell, caseD, caseTop - caseBottom, caseBottom, "Case");
            MeshPart(slot, Dome, shell, new Vector3(caseD, 2f * domeH, caseD), new Vector3(0f, caseTop, 0f), "ForwardDome");
            Cylinder(slot, c.Dark, 0.35f * caseD, Mathf.Max(0.02f, h - caseTop - domeH), caseTop + domeH, "ForwardAttach");
            float len = caseTop - caseBottom;
            int joints = Mathf.Max(small ? 0 : 1, Mathf.RoundToInt(len / 3.2f) - 1);
            for (int k = 1; k <= joints; k++)
                Ring(slot, large ? c.Grey : c.Dark, 1.03f * caseD, Mathf.Min(0.18f, 0.03f * len), caseBottom + len * k / (joints + 1), "FieldJoint" + k);
            Ring(slot, c.Dark, 1.03f * caseD, Mathf.Min(0.15f, 0.04f * len), caseBottom, "AftRing");
            // Systems tunnel: the cable run along the case.
            Box(slot, c.Grey, new Vector3(0.06f * d, 0.85f * len, 0.05f * d), new Vector3(0.5f * caseD + 0.02f * d, caseBottom + 0.5f * len, 0f), "Tunnel");
            if (large) Ring(slot, c.Signal, 1.01f * caseD, Mathf.Min(0.3f, 0.02f * h), caseTop - 0.1f * len, "Band");
        }

        /// <summary>Nuclear thermal engine: thrust frame, shadow shield, reactor vessel, turbopumps, radiator fins, a large bell.</summary>
        static void NuclearEngine(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h, RocketModel model)
        {
            bool large = m != null && m.id == ModuleCatalog.EngineNuclearLarge;
            float bellH = 0.45f * h;
            float ext = 0.5f * bellH;
            MeshPart(slot, NozzleExtension, c.Niobium, new Vector3(d, ext, d), Vector3.zero, "NozzleExtension");
            float upper = d * (0.5f - 0.18f) * 2f;
            MeshPart(slot, Bell, c.Copper, new Vector3(upper, bellH - ext, upper), new Vector3(0f, ext, 0f), "Bell");
            float vessel = 0.55f * d, vb = 0.5f * h, vt = 0.8f * h;
            Cylinder(slot, c.Steel, 0.3f * d, vb - bellH + 0.02f * h, bellH - 0.01f * h, "Throat");
            Cylinder(slot, c.Steel, vessel, vt - vb, vb, "ReactorVessel");
            MeshPart(slot, Dome, c.Steel, new Vector3(vessel, 0.12f * vessel, vessel), new Vector3(0f, vt, 0f), "VesselHead");
            for (int k = 0; k < 3; k++) Ring(slot, c.Dark, 1.02f * vessel, 0.025f * h, vb + (vt - vb) * (0.2f + 0.3f * k), "Band" + k);
            Ring(slot, c.Signal, 1.02f * vessel, 0.03f * h, vb + 0.55f * (vt - vb), "Warning");
            Mesh shield = ProceduralMeshes.Frustum(32, 0.5f * vessel, 0.42f * d, true, true, "ShadowShield");
            model.OwnedMeshes.Add(shield);
            MeshPart(slot, shield, c.ShieldHeavy, new Vector3(1f, 0.06f * h, 1f), new Vector3(0f, vt + 0.02f * h, 0f), "ShadowShield");
            float frame = vt + 0.08f * h;
            for (int k = 0; k < 4; k++)
            {
                float a = (45f + 90f * k) * Mathf.Deg2Rad;
                Rod(slot, c.Steel, new Vector3(0.3f * d * Mathf.Sin(a), frame, 0.3f * d * Mathf.Cos(a)),
                    new Vector3(0.44f * d * Mathf.Sin(a), h - 0.03f * h, 0.44f * d * Mathf.Cos(a)), 0.04f * d, "Thrust" + k);
            }
            Cylinder(slot, c.Dark, 0.92f * d, 0.03f * h, h - 0.03f * h, "MountPlate");
            int pumps = large ? 2 : 1;
            for (int k = 0; k < pumps; k++)
            {
                float side = k == 0 ? 1f : -1f;
                Cylinder(slot, c.Dark, 0.14f * d, 0.12f * h, frame, "Turbopump" + k, side * 0.3f * d, 0f);
                Rod(slot, c.Steel, new Vector3(side * 0.3f * d, frame, 0f), new Vector3(side * 0.16f * d, bellH, 0f), 0.04f * d, "FeedLine" + k);
            }
            int radiators = large ? 4 : 2;
            for (int k = 0; k < radiators; k++)
            {
                Quaternion around = Quaternion.Euler(0f, 360f * k / radiators + 90f, 0f); // beside the pumps
                GameObject fin = Box(slot, c.Radiator, new Vector3(0.02f * d, 0.28f * h, 0.2f * d), around * new Vector3(0f, vb + 0.5f * (vt - vb), 0.37f * d), "Radiator" + k);
                fin.transform.localRotation = around;
            }
        }

        /// <summary>Hall ring, gridded ion engine or magnetoplasma coils: small, every one a different shape.</summary>
        static void ElectricThruster(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h, RocketModel model)
        {
            string id = m != null ? m.id : ModuleCatalog.EngineHall;
            if (id == ModuleCatalog.EngineMagnetoplasma)
            {
                // Anode nozzle flaring downward inside a stack of copper coils; body and radiators above.
                Mesh anode = ProceduralMeshes.Frustum(28, 0.38f * d, 0.12f * d, false, false, "Anode");
                model.OwnedMeshes.Add(anode);
                MeshPart(slot, anode, c.Copper, new Vector3(1f, 0.6f * h, 1f), Vector3.zero, "Anode");
                Cylinder(slot, c.Niobium, 0.05f * d, 0.55f * h, 0.05f * h, "Cathode");
                for (int k = 0; k < 5; k++)
                {
                    float y = (0.12f + 0.09f * k) * h, size = Mathf.Lerp(0.95f, 0.6f, k / 4f) * d;
                    MeshPart(slot, TorusFat, c.Copper, new Vector3(size, size, size), new Vector3(0f, y, 0f), "Coil" + k);
                }
                Cylinder(slot, c.Steel, 0.6f * d, 0.33f * h, 0.6f * h, "Body");
                for (int k = 0; k < 4; k++)
                {
                    Quaternion around = Quaternion.Euler(0f, 45f + 90f * k, 0f);
                    GameObject fin = Box(slot, c.Radiator, new Vector3(0.02f * d, 0.3f * h, 0.18f * d), around * new Vector3(0f, 0.77f * h, 0.39f * d), "Radiator" + k);
                    fin.transform.localRotation = around;
                }
                Cylinder(slot, c.Dark, 0.3f * d, 0.07f * h, 0.93f * h, "Mount");
                return;
            }
            if (id == ModuleCatalog.EngineIon)
            {
                // Discharge chamber in a gold blanket, the flat extraction grids facing down, a neutraliser on the side.
                Cylinder(slot, c.Gold, 0.9f * d, 0.45f * h, 0.38f * h, "Chamber");
                Mesh mouth = ProceduralMeshes.Frustum(28, 0.42f * d, 0.45f * d, false, false, "Mouth");
                model.OwnedMeshes.Add(mouth);
                MeshPart(slot, mouth, c.Steel, new Vector3(1f, 0.1f * h, 1f), new Vector3(0f, 0.28f * h, 0f), "Mouth");
                Cylinder(slot, c.Dark, 0.82f * d, 0.02f * h, 0.27f * h, "Grid");
                float gridR = 0.4f * d;
                for (int k = -3; k <= 3; k++)
                {
                    float off = gridR * k / 3.5f;
                    float chord = 2f * Mathf.Sqrt(Mathf.Max(0f, gridR * gridR - off * off));
                    Box(slot, c.Steel, new Vector3(chord, 0.012f * h, 0.015f * d), new Vector3(0f, 0.265f * h, off), "GridX" + k);
                    Box(slot, c.Steel, new Vector3(0.015f * d, 0.012f * h, chord), new Vector3(off, 0.265f * h, 0f), "GridZ" + k);
                }
                Ring(slot, c.Steel, 0.94f * d, 0.06f * h, 0.24f * h, "Rim");
                AxisCylinder(slot, c.Dark, 0.09f * d, 0.3f * h, new Vector3(0.46f * d, 0.3f * h, 0f), new Vector3(0.3f, 1f, 0f), "Neutraliser");
                Cylinder(slot, c.Dark, 0.35f * d, 0.17f * h, 0.83f * h, "Gimbal");
                return;
            }
            // Hall thruster: a white ceramic ring channel between the inner and outer magnetic poles.
            Cylinder(slot, c.Steel, 0.95f * d, 0.38f * h, 0.04f * h, "OuterPole");
            Ring(slot, c.Ceramic, 0.72f * d, 0.05f * h, 0.0f, "Channel");
            MeshPart(slot, TorusThin, c.Ceramic, new Vector3(0.7f * d, 0.7f * d, 0.7f * d), new Vector3(0f, 0.03f * h, 0f), "ChannelLip");
            Cylinder(slot, c.Steel, 0.36f * d, 0.42f * h, 0f, "InnerPole");
            for (int k = 0; k < 4; k++)
            {
                float a = (45f + 90f * k) * Mathf.Deg2Rad;
                Cylinder(slot, c.Copper, 0.13f * d, 0.3f * h, 0.08f * h, "Coil" + k, 0.4f * d * Mathf.Sin(a), 0.4f * d * Mathf.Cos(a));
            }
            AxisCylinder(slot, c.Dark, 0.08f * d, 0.35f * h, new Vector3(0f, 0.22f * h, 0.47f * d), new Vector3(0f, 1f, -0.25f), "Cathode");
            Cylinder(slot, c.Dark, 0.6f * d, 0.15f * h, 0.42f * h, "Body");
            Cylinder(slot, c.Steel, 0.25f * d, 0.43f * h, 0.57f * h, "Gimbal");
        }

        // ---- Tanks ----------------------------------------------------------------------------------------------

        static void Tank(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h)
        {
            PropellantType fuel = m != null ? m.propellant : PropellantType.Kerolox;
            Material body = fuel == PropellantType.Hydrolox ? c.Foam : fuel == PropellantType.Hypergolic ? c.Grey : c.White;
            Cylinder(slot, body, d, h, 0f, "Tank");
            float joint = Mathf.Min(0.18f, 0.1f * h);
            MeshPart(slot, Tube, c.Dark, new Vector3(1.008f * d, joint, 1.008f * d), new Vector3(0f, h - joint, 0f), "Joint");
            if (fuel == PropellantType.Kerolox && h > 4f)
                MeshPart(slot, Tube, c.Dark, new Vector3(1.006f * d, 0.5f, 1.006f * d), new Vector3(0f, 0.72f * h - 0.25f, 0f), "RollBand");
            if (fuel == PropellantType.Hypergolic)
                for (int k = 0; k < 2; k++)
                    MeshPart(slot, Tube, c.Signal, new Vector3(1.006f * d, 0.12f, 1.006f * d), new Vector3(0f, (0.3f + 0.35f * k) * h, 0f), "Band" + k);
            if (m != null && m.requiresPurchase) // an upgrade from the shop wears a blue stripe
                MeshPart(slot, Tube, c.Accent, new Vector3(1.007f * d, 0.35f, 1.007f * d), new Vector3(0f, 0.4f * h, 0f), "UpgradeStripe");
        }

        /// <summary>Liquid hydrogen: pale insulation with stringer rings every 2 m and vent lines along the side.</summary>
        static void HydrogenTank(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h)
        {
            Cylinder(slot, c.Insulation, d, h, 0f, "Tank");
            int rings = Mathf.Max(1, Mathf.RoundToInt(h / 2f));
            for (int k = 1; k < rings; k++) Ring(slot, c.Grey, 1.005f * d, 0.06f, h * k / rings - 0.03f, "Stringer" + k);
            float joint = Mathf.Min(0.18f, 0.1f * h);
            Ring(slot, c.Dark, 1.008f * d, joint, h - joint, "Joint");
            Ring(slot, c.Dark, 1.008f * d, joint, 0f, "LowerJoint");
            int lines = h > 20f ? 2 : 1;
            for (int k = 0; k < lines; k++)
            {
                float a = (60f + 180f * k) * Mathf.Deg2Rad;
                float rr = 0.5f * d + 0.06f;
                Box(slot, c.Steel, new Vector3(0.1f, h - 2f * joint, 0.1f), new Vector3(rr * Mathf.Sin(a), 0.5f * h, rr * Mathf.Cos(a)), "VentLine" + k);
            }
            Box(slot, c.Grey, new Vector3(0.3f, 0.25f * Mathf.Min(h, 8f), 0.12f), new Vector3(0f, 0.15f * h, 0.5f * d + 0.04f), "Cableway");
        }

        /// <summary>Xenon: overwrapped spheres held in an open frame.</summary>
        static void XenonTank(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h)
        {
            float r = 0.5f * d;
            Ring(slot, c.Dark, d, Mathf.Min(0.12f, 0.12f * h), 0f, "LowerRing");
            Ring(slot, c.Dark, d, Mathf.Min(0.12f, 0.12f * h), h - Mathf.Min(0.12f, 0.12f * h), "UpperRing");
            Cylinder(slot, c.Interior, 0.98f * d, 0.04f, 0.02f, "Deck");
            for (int k = 0; k < 8; k++)
            {
                float a = (22.5f + 45f * k) * Mathf.Deg2Rad;
                Box(slot, c.Steel, new Vector3(0.07f, h, 0.07f), new Vector3(0.97f * r * Mathf.Sin(a), 0.5f * h, 0.97f * r * Mathf.Cos(a)), "Post" + k);
            }
            float s = Mathf.Min(0.85f * h, 0.9f * r);
            float ring = r - 0.5f * s - 0.08f;
            int n = Mathf.Clamp((int)(2f * Mathf.PI * ring / (1.08f * s)), 3, 8);
            for (int k = 0; k < n; k++)
            {
                float a = (360f * k / n) * Mathf.Deg2Rad;
                var centre = new Vector3(ring * Mathf.Sin(a), 0.5f * h, ring * Mathf.Cos(a));
                Ball(slot, c.Carbon, s, centre, "Sphere" + k);
                Cylinder(slot, c.Steel, 0.18f * s, 0.12f * s, centre.y + 0.45f * s, "Boss" + k, centre.x, centre.z);
            }
            if (ring - 0.5f * s > 0.3f * s) Ball(slot, c.Carbon, Mathf.Min(s, 2f * (ring - 0.5f * s) - 0.05f), new Vector3(0f, 0.5f * h, 0f), "CentreSphere");
            Box(slot, c.Gold, new Vector3(0.4f * s, 0.3f * s, 0.25f * s), new Vector3(0f, 0.12f * h + 0.15f * s, 0.85f * r), "Regulator");
        }

        // ---- Cabins ---------------------------------------------------------------------------------------------

        static void Bus(Transform slot, RocketPalette c, float d, float h, RocketModel model)
        {
            float deck = Mathf.Min(0.15f, 0.07f * h);
            // Turned so flat faces point along ±X / ±Z: the solar wings fold flat against the ±X faces.
            MeshPart(slot, Octagon, c.Gold, new Vector3(d, h - 2f * deck, d), new Vector3(0f, deck, 0f), "Bus").transform.localRotation =
                Quaternion.Euler(0f, 22.5f, 0f);
            float face = 0.5f * d * Mathf.Cos(22.5f * Mathf.Deg2Rad);
            Cylinder(slot, c.White, 1.02f * d, deck, 0f, "LowerDeck");
            Cylinder(slot, c.White, 1.02f * d, deck, h - deck, "UpperDeck");
            MeshPart(slot, Dish, c.White, new Vector3(0.55f * d, 0.12f * d, 0.55f * d), new Vector3(0f, h, 0f), "Antenna");
            Cylinder(slot, c.Dark, 0.04f, 0.22f * d, h, "Feed");
            for (int side = -1; side <= 1; side += 2)
            {
                var stowed = Box(slot, c.Panel, new Vector3(0.03f, 0.8f * h, 0.34f * d), new Vector3(side * (face + 0.02f), 0.5f * h, 0f), "WingStowed");
                model.StowedWings.Add(stowed);
                var deployed = new GameObject("WingDeployed");
                deployed.transform.SetParent(slot, false);
                Box(deployed.transform, c.Steel, new Vector3(0.45f * d, 0.05f, 0.05f), new Vector3(side * (0.5f * d + 0.22f * d), 0.5f * h, 0f), "Boom");
                Box(deployed.transform, c.Panel, new Vector3(2.4f * d, 0.04f, 0.7f * h), new Vector3(side * (0.5f * d + 0.45f * d + 1.2f * d), 0.5f * h, 0f), "Array");
                model.DeployedWings.Add(deployed);
                deployed.SetActive(false);
            }
        }

        /// <summary>The small probe: a silver hexagonal body with cells on three faces, a small dish and two whip antennas.</summary>
        static void SmallProbe(Transform slot, RocketPalette c, float d, float h)
        {
            float deck = Mathf.Min(0.08f, 0.1f * h);
            MeshPart(slot, Hexagon, c.Steel, new Vector3(d, h - 2f * deck, d), new Vector3(0f, deck, 0f), "Body");
            Cylinder(slot, c.Gold, 0.95f * d, deck, 0f, "LowerDeck");
            Cylinder(slot, c.White, 0.95f * d, deck, h - deck, "UpperDeck");
            float face = 0.5f * d * Mathf.Cos(30f * Mathf.Deg2Rad);
            for (int k = 0; k < 3; k++)
            {
                Quaternion around = Quaternion.Euler(0f, 30f + 120f * k, 0f);
                GameObject cells = Box(slot, c.Panel, new Vector3(0.42f * d, 0.7f * (h - 2f * deck), 0.015f), around * new Vector3(0f, 0.5f * h, face + 0.008f), "Cells" + k);
                cells.transform.localRotation = around;
            }
            Cylinder(slot, c.Dark, 0.06f * d, 0.2f * h, h, "Mast");
            GameObject dishGo = MeshPart(slot, Dish, c.White, new Vector3(0.45f * d, 0.1f * d, 0.45f * d), new Vector3(0f, 1.2f * h, 0.05f * d), "Dish");
            dishGo.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            for (int k = -1; k <= 1; k += 2)
                Cylinder(slot, c.Dark, 0.015f, 0.9f * h, h, "Whip", k * 0.38f * d, -0.1f * d);
        }

        /// <summary>
        /// Crewed capsule: a blunt body of revolution (cone for the small ones, cylinder-and-cone for the big cabin), windows set in
        /// the wall, the side hatch at the front (+Z), a docking tunnel on top and four small thruster pods.
        /// </summary>
        static void Capsule(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h, RocketModel model)
        {
            string id = m != null ? m.id : ModuleCatalog.CabinCrew1;
            bool big = id == ModuleCatalog.CabinCrew7;
            int windows = id == ModuleCatalog.CabinCrew1 ? 1 : id == ModuleCatalog.CabinCrew3 ? 3 : 6;
            // Profile in units of the slot (radius 0.5 = d/2, height 1 = h).
            float shoulder = big ? 0.42f : 0.08f, coneTop = 0.82f, topR = big ? 0.2f : 0.17f, tunnelR = 0.13f;
            var profile = new List<Vector2>
            {
                new Vector2(0.001f, 0f), new Vector2(0.3f, 0.005f), new Vector2(0.45f, 0.02f), new Vector2(0.5f, 0.05f),
                new Vector2(0.5f, shoulder), new Vector2(0.499f, shoulder + 0.01f)
            };
            if (big) profile.Add(new Vector2(0.47f, shoulder + 0.06f));
            profile.Add(new Vector2(topR + 0.01f, coneTop - 0.01f));
            profile.Add(new Vector2(topR, coneTop));
            profile.Add(new Vector2(tunnelR, coneTop + 0.005f));
            profile.Add(new Vector2(tunnelR, 0.98f));
            profile.Add(new Vector2(0.11f, 1f));
            profile.Add(new Vector2(0.001f, 1f));
            Mesh body = ProceduralMeshes.Lathe(48, profile.ToArray(), false, "Capsule");
            model.OwnedMeshes.Add(body);
            MeshPart(slot, body, big ? c.Grey : c.White, new Vector3(d, h, d), Vector3.zero, "Body");
            Ring(slot, c.Dark, 1.01f * d, 0.03f * h, 0.035f * h, "ShieldSeam");
            Ring(slot, c.Steel, 2f * tunnelR * d * 1.15f, 0.04f * h, 0.94f * h, "DockingRing");

            // Wall slope of the cone: windows and the hatch lean back with it.
            float coneBottom = big ? shoulder + 0.06f : shoulder, rBottom = big ? 0.47f : 0.5f;
            float slope = Mathf.Atan2((rBottom - topR) * d, (coneTop - coneBottom) * h) * Mathf.Rad2Deg;
            Vector3 OnCone(float t, float azimuth, out Quaternion rot)
            {
                float y = coneBottom + t * (coneTop - coneBottom);
                float rr = Mathf.Lerp(rBottom, topR, t) * d;
                float a = azimuth * Mathf.Deg2Rad;
                rot = Quaternion.Euler(-slope, azimuth, 0f);
                return new Vector3(rr * Mathf.Sin(a), y * h, rr * Mathf.Cos(a));
            }
            float win = Mathf.Clamp(0.12f * d, 0.18f, 0.4f);
            for (int k = 0; k < windows; k++)
            {
                int row = big && k >= 3 ? 1 : 0;
                float azimuth = (big ? 60f + 80f * (k % 3) : windows == 1 ? 70f : 50f + 50f * k) + (row == 1 ? 40f : 0f);
                Vector3 pos = OnCone(row == 1 ? 0.62f : 0.38f, azimuth, out Quaternion rot);
                if (big && row == 0) // on the cylinder
                {
                    float a = azimuth * Mathf.Deg2Rad;
                    pos = new Vector3(0.5f * d * Mathf.Sin(a), 0.5f * shoulder * h + 0.05f * h, 0.5f * d * Mathf.Cos(a));
                    rot = Quaternion.Euler(0f, azimuth, 0f);
                }
                GameObject frame = Box(slot, c.Dark, new Vector3(1.3f * win, 1.1f * win, 0.04f), pos, "WindowFrame" + k);
                frame.transform.localRotation = rot;
                GameObject glass = Box(slot, c.Glass, new Vector3(win, 0.8f * win, 0.03f), pos + rot * new Vector3(0f, 0f, 0.012f), "Window" + k);
                glass.transform.localRotation = rot;
            }
            // Hatch at the front.
            float hatchW = Mathf.Clamp(0.3f * d, 0.6f, 1.0f), hatchH = Mathf.Clamp(0.32f * h, 0.7f, 1.1f);
            Vector3 hp = OnCone(big ? 0.25f : 0.35f, 0f, out Quaternion hr);
            if (big)
            {
                hp = new Vector3(0f, 0.5f * shoulder * h + 0.02f * h, 0.5f * d);
                hr = Quaternion.identity;
            }
            GameObject hatch = Box(slot, c.Grey, new Vector3(hatchW, hatchH, 0.05f), hp, "Hatch");
            hatch.transform.localRotation = hr;
            GameObject handle = Box(slot, c.Signal, new Vector3(0.35f * hatchW, 0.05f, 0.05f), hp + hr * new Vector3(0f, -0.25f * hatchH, 0.05f), "HatchHandle");
            handle.transform.localRotation = hr;
            AxisCylinder(slot, c.Glass, 0.3f * hatchW, 0.04f, hp + hr * new Vector3(0f, 0.18f * hatchH, 0.03f), hr * Vector3.forward, "HatchPort");
            // Thruster pods on the upper cone, between the windows.
            for (int k = 0; k < 4; k++)
            {
                Vector3 pp = OnCone(0.78f, 45f + 90f * k, out Quaternion pr);
                GameObject pod = Box(slot, c.Dark, new Vector3(0.14f * d, 0.1f * h, 0.06f * d), pp, "RcsPod" + k);
                pod.transform.localRotation = pr;
            }
            Anchor(slot, model, new Vector3(0f, 0.86f * h, (topR + 0.02f) * d), 25f); // a docking light over the hatch
        }

        // ---- Payload equipment ----------------------------------------------------------------------------------

        static void Wrap(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h, float thickness)
        {
            Material shell = m != null && m.acidResistance > 0 && m.radiationShielding == 0 ? c.Ceramic
                : m != null && m.radiationShielding >= 2 ? c.ShieldHeavy
                : m != null && m.radiationShielding > 0 ? c.Shield : c.Grey;
            MeshPart(slot, Tube, shell, new Vector3(d, h, d), Vector3.zero, "Shell");
            MeshPart(slot, Tube, c.Dark, new Vector3(d + 0.03f, 0.08f, d + 0.03f), Vector3.zero, "LowerRim");
            MeshPart(slot, Tube, c.Dark, new Vector3(d + 0.03f, 0.08f, d + 0.03f), new Vector3(0f, h - 0.08f, 0f), "UpperRim");
            if (m != null && m.radiationShielding > 0)
            {
                int plates = m.radiationShielding >= 2 ? 12 : 8;
                for (int k = 0; k < plates; k++)
                {
                    Quaternion around = Quaternion.Euler(0f, 360f * k / plates, 0f);
                    GameObject seam = Box(slot, c.Dark, new Vector3(0.04f, h - 0.16f, Mathf.Max(0.03f, thickness)), around * new Vector3(0f, 0.5f * h, 0.5f * d), "Seam" + k);
                    seam.transform.localRotation = around;
                }
            }
        }

        /// <summary>Ablative heat shield: a shallow dome under the capsule, its rim and the structural back plate.</summary>
        static void HeatShield(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h, RocketModel model)
        {
            bool heavy = m != null && m.heatFluxLimit >= 3e6;
            Vector2[] profile =
            {
                new Vector2(0.001f, 0f), new Vector2(0.2f, 0.06f), new Vector2(0.35f, 0.18f), new Vector2(0.45f, 0.34f),
                new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.8f), new Vector2(0.44f, 1f), new Vector2(0.001f, 1f)
            };
            Mesh shield = ProceduralMeshes.Lathe(48, profile, false, "HeatShield");
            model.OwnedMeshes.Add(shield);
            MeshPart(slot, shield, heavy ? c.Carbon : c.Ablator, new Vector3(d, h, d), Vector3.zero, "Ablator");
            Ring(slot, c.Metal, 1.01f * d, 0.25f * h, 0.55f * h, "Rim");
            if (heavy)
                for (int k = 0; k < 12; k++)
                {
                    Quaternion around = Quaternion.Euler(0f, 30f * k, 0f);
                    GameObject seam = Box(slot, c.Ablator, new Vector3(0.02f, 0.5f * h, 0.03f), around * new Vector3(0f, 0.3f * h, 0.44f * d), "TileSeam" + k);
                    seam.transform.localRotation = around;
                }
        }

        static void ParachuteContainer(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h)
        {
            string id = m != null ? m.id : ModuleCatalog.ParachuteSmall;
            Cylinder(slot, id == ModuleCatalog.ParachuteSmall ? c.White : c.Grey, 0.9f * d, 0.7f * h, 0f, "Can");
            MeshPart(slot, Dome, c.CanopyOrange, new Vector3(0.9f * d, 0.6f * h, 0.9f * d), new Vector3(0f, 0.7f * h, 0f), "Lid");
            Ring(slot, c.Dark, 0.92f * d, 0.06f * h, 0.62f * h, "LidRing");
            int straps = id == ModuleCatalog.ParachuteLarge ? 3 : id == ModuleCatalog.ParachuteMedium ? 2 : 1;
            for (int k = 0; k < straps; k++) Ring(slot, c.Dark, 0.915f * d, 0.05f * h, (0.15f + 0.4f * k / Mathf.Max(1, straps)) * h, "Strap" + k);
            if (id == ModuleCatalog.ParachuteLarge)
                for (int k = 0; k < 3; k++)
                {
                    Quaternion around = Quaternion.Euler(0f, 120f * k, 0f);
                    GameObject riser = Box(slot, c.Signal, new Vector3(0.06f * d, 0.7f * h, 0.03f), around * new Vector3(0f, 0.35f * h, 0.46f * d), "Riser" + k);
                    riser.transform.localRotation = around;
                }
        }

        /// <summary>
        /// The canopy of a parachute: hidden until <see cref="RocketModel.SetParachutesDeployed"/>. Real size from the catalog area
        /// (A = π·D²/4), on lines about 1.1 D long; several canopies lean apart.
        /// </summary>
        static void Canopy(Transform slot, RocketPalette c, ModuleDefinition m, PartPlacement p, RocketModel model)
        {
            double area = m != null && m.parachuteArea > 0.0 ? m.parachuteArea : 50.0;
            float diameter = (float)Math.Sqrt(4.0 * area / Math.PI);
            float lines = 1.1f * diameter;
            float lean = p.RadialOffset > 1e-6 ? 14f : 0f;
            GameObject root = Group(slot, "CanopyDeployed", new Vector3(0f, (float)p.Height, 0f), Quaternion.Euler(lean, 0f, 0f));
            GameObject inflate = Group(root.transform, "Inflate", Vector3.zero, Quaternion.identity);
            Material cloth = m != null && m.id == ModuleCatalog.ParachuteSmall ? c.CanopyWhite : c.CanopyOrange;
            MeshPart(inflate.transform, CanopyMesh, cloth, new Vector3(diameter, diameter, diameter), new Vector3(0f, lines, 0f), "Canopy");
            MeshPart(inflate.transform, Tube, c.CanopyWhite, new Vector3(1.002f * diameter, 0.05f * diameter, 1.002f * diameter),
                new Vector3(0f, lines, 0f), "SkirtBand");
            MeshPart(inflate.transform, Tube, c.CanopyWhite, new Vector3(0.75f * diameter, 0.04f * diameter, 0.75f * diameter),
                new Vector3(0f, lines + 0.26f * diameter, 0f), "CrownBand");
            const int n = 12;
            float thick = Mathf.Max(0.02f, 0.002f * diameter);
            for (int k = 0; k < n; k++)
            {
                float a = 2f * Mathf.PI * k / n;
                Rod(inflate.transform, c.CanopyWhite, Vector3.zero, new Vector3(0.5f * diameter * Mathf.Sin(a), lines, 0.5f * diameter * Mathf.Cos(a)), thick, "Line" + k);
            }
            root.SetActive(false);
            model.Canopies.Add(new RocketModel.CanopyRig { Root = root, Inflate = inflate.transform });
        }

        /// <summary>
        /// Cargo: open bays show their cavity — frames (шпангоуты) every ~0.9 m, longerons, a floor and partial skin; the
        /// pressurised module is closed, with domed ends and a hatch.
        /// </summary>
        static void CargoBay(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h, RocketModel model)
        {
            string id = m != null ? m.id : ModuleCatalog.Cargo2;
            if (m != null && m.pressurized)
            {
                float end = Mathf.Min(0.12f * h, 0.25f * d);
                Cylinder(slot, c.White, 0.98f * d, h - 2f * end, end, "Hull");
                MeshPart(slot, Dome, c.White, new Vector3(0.98f * d, 2f * end, 0.98f * d), new Vector3(0f, h - end, 0f), "UpperDome");
                GameObject lower = MeshPart(slot, Dome, c.White, new Vector3(0.98f * d, 2f * end, 0.98f * d), new Vector3(0f, end, 0f), "LowerDome");
                lower.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                Ring(slot, c.Dark, d, 0.08f, 0f, "LowerRing");
                Ring(slot, c.Dark, d, 0.08f, h - 0.08f, "UpperRing");
                for (int k = 1; k <= 3; k++) Ring(slot, c.Grey, 0.99f * d, 0.04f, end + (h - 2f * end) * k / 4f, "PanelSeam" + k);
                float hatch = Mathf.Min(1.0f, 0.4f * d);
                Box(slot, c.Grey, new Vector3(hatch, 1.2f * hatch, 0.06f), new Vector3(0f, 0.45f * h, 0.49f * d), "Hatch");
                AxisCylinder(slot, c.Glass, 0.35f * hatch, 0.05f, new Vector3(0f, 0.45f * h + 0.3f * hatch, 0.5f * d + 0.02f), Vector3.forward, "Porthole");
                for (int k = -1; k <= 1; k += 2)
                    Box(slot, c.Signal, new Vector3(0.04f, 0.7f * h, 0.04f), new Vector3(k * 0.7f * hatch, 0.45f * h, 0.5f * d + 0.04f), "Handrail");
                return;
            }
            bool large = id == ModuleCatalog.Cargo40, medium = id == ModuleCatalog.Cargo12;
            int frames = Mathf.Max(1, Mathf.RoundToInt(h / 0.9f));
            for (int k = 0; k <= frames; k++)
                Ring(slot, c.Steel, d, 0.08f, Mathf.Min(h - 0.08f, h * k / frames), "Frame" + k);
            int longerons = large ? 12 : medium ? 10 : 8;
            for (int k = 0; k < longerons; k++)
            {
                float a = (360f * k / longerons + 180f / longerons) * Mathf.Deg2Rad;
                Box(slot, c.Grey, new Vector3(0.06f, h, 0.06f), new Vector3(0.48f * d * Mathf.Sin(a), 0.5f * h, 0.48f * d * Mathf.Cos(a)), "Longeron" + k);
            }
            Cylinder(slot, c.Interior, 0.96f * d, 0.05f, 0.02f, "Floor");
            for (int k = -1; k <= 1; k++) Box(slot, c.Dark, new Vector3(0.9f * d, 0.02f, 0.03f), new Vector3(0f, 0.075f, k * 0.25f * d), "TieDown" + k);
            void Skin(float from, float to, string name)
            {
                Mesh outside = ArcShell(from, to, true, name + "Out"), inside = ArcShell(from, to, false, name + "In");
                model.OwnedMeshes.Add(outside);
                model.OwnedMeshes.Add(inside);
                MeshPart(slot, outside, c.White, new Vector3(d, h, d), Vector3.zero, name + "Out");
                MeshPart(slot, inside, c.Interior, new Vector3(0.985f * d, h, 0.985f * d), Vector3.zero, name + "In");
            }
            if (large)
            {
                // Open truss: X-bracing between the frames on every second bay.
                for (int k = 0; k < frames; k++)
                    for (int j = 0; j < longerons; j += 2)
                    {
                        float a0 = (360f * j / longerons + 180f / longerons) * Mathf.Deg2Rad, a1 = (360f * (j + 1) / longerons + 180f / longerons) * Mathf.Deg2Rad;
                        float y0 = h * k / frames, y1 = h * (k + 1) / frames, rr = 0.48f * d;
                        Rod(slot, c.Steel, new Vector3(rr * Mathf.Sin(a0), y0, rr * Mathf.Cos(a0)), new Vector3(rr * Mathf.Sin(a1), y1, rr * Mathf.Cos(a1)), 0.03f, "Brace" + k + "_" + j);
                    }
            }
            else if (medium)
            {
                Skin(30f, 150f, "SkinRight");
                Skin(210f, 330f, "SkinLeft");
            }
            else Skin(50f, 310f, "Skin");
        }

        /// <summary>Rover frame: two side rails, cross members, a deck, and a suspension arm out to each wheel of the design.</summary>
        static void Chassis(Transform slot, RocketPalette c, PartPlacement p, float f, float h, RocketLayout layout)
        {
            float w = 0.7f * f;
            for (int k = -1; k <= 1; k += 2)
                Box(slot, c.Steel, new Vector3(0.06f * f, 0.35f * h, 0.95f * f), new Vector3(k * 0.33f * f, 0.5f * h, 0f), "Rail");
            for (int k = -1; k <= 1; k++)
                Box(slot, c.Steel, new Vector3(0.66f * f, 0.2f * h, 0.05f * f), new Vector3(0f, 0.5f * h, k * 0.42f * f), "Cross" + (k + 1));
            Box(slot, c.Grey, new Vector3(w * 0.9f, 0.06f, 0.85f * f), new Vector3(0f, 0.75f * h, 0f), "Deck");
            Box(slot, c.Gold, new Vector3(0.3f * f, 0.2f * h, 0.25f * f), new Vector3(0f, 0.75f * h + 0.1f * h + 0.03f, -0.2f * f), "Avionics");
            Box(slot, c.Signal, new Vector3(0.66f * f, 0.05f * h, 0.02f), new Vector3(0f, 0.5f * h, 0.5f * f), "Bumper");
            if (layout == null)
            {
                for (int k = 0; k < 4; k++) // a lone frame (thumbnail): stub arms at the corners
                    Rod(slot, c.Steel, new Vector3((k % 2 == 0 ? 1 : -1) * 0.33f * f, 0.5f * h, (k < 2 ? 1 : -1) * 0.4f * f),
                        new Vector3((k % 2 == 0 ? 1 : -1) * 0.5f * f, 0.3f * h, (k < 2 ? 1 : -1) * 0.45f * f), 0.05f * f, "Arm" + k);
                return;
            }
            int n = 0;
            foreach (PartPlacement q in layout.Parts)
            {
                if (q.Role != PartRole.Wheel) continue;
                float x = (float)q.X, z = (float)q.Z, y = (float)(q.Center - p.Bottom);
                float side = x >= 0f ? 1f : -1f;
                var hub = new Vector3(x - side * 0.3f * (float)q.Diameter, y, z);
                var root = new Vector3(side * 0.33f * f, 0.5f * h, Mathf.Clamp(z, -0.45f * f, 0.45f * f));
                Rod(slot, c.Steel, root, hub, 0.05f * f, "Arm" + n);
                Cylinder(slot, c.Dark, 0.1f * f, 0.12f * h, 0.5f * h - 0.06f * h, "Pivot" + n, root.x, root.z);
                n++;
            }
        }

        static void Drive(Transform slot, RocketPalette c, float d, float h)
        {
            AxisCylinder(slot, c.Steel, 0.5f * Mathf.Min(d, 2f * h), 0.8f * d, new Vector3(0f, 0.5f * h, 0f), Vector3.right, "Motor");
            Box(slot, c.Dark, new Vector3(0.35f * d, 0.8f * h, 0.45f * d), new Vector3(0f, 0.45f * h, -0.2f * d), "Gearbox");
            for (int k = -1; k <= 1; k += 2)
                AxisCylinder(slot, c.Grey, 0.25f * Mathf.Min(d, 2f * h), 0.1f * d, new Vector3(k * 0.45f * d, 0.5f * h, 0f), Vector3.right, "Flange");
            Box(slot, c.Signal, new Vector3(0.06f * d, 0.1f * h, 0.4f * d), new Vector3(0.2f * d, 0.9f * h, 0.1f * d), "Harness");
            for (int k = 0; k < 4; k++)
                Box(slot, c.Radiator, new Vector3(0.02f * d, 0.5f * h, 0.3f * d), new Vector3((-0.15f + 0.1f * k) * d, 0.75f * h, 0.25f * d), "Fin" + k);
        }

        /// <summary>The wheel's own frame: at the wheel centre, local X along the rocket's X axis (the axle).</summary>
        static Transform WheelFrame(Transform slot, PartPlacement p, RocketModel model)
        {
            GameObject axle = Group(slot, "Axle", new Vector3(0f, 0.5f * (float)p.Height, 0f), Quaternion.Euler(0f, -(float)p.AngleDegrees, 0f));
            GameObject wheel = Group(axle.transform, "Wheel", Vector3.zero, Quaternion.identity);
            model.WheelTransforms.Add(wheel.transform);
            return wheel.transform;
        }

        static void Wheel(Transform wheel, RocketPalette c, ModuleDefinition m, float d)
        {
            bool large = m != null && m.id == ModuleCatalog.WheelLarge;
            float w = (large ? 0.45f : 0.4f) * d;
            if (large)
            {
                AxisCylinder(wheel, c.Rubber, d * 0.97f, w, Vector3.zero, Vector3.right, "Tyre");
                AxisCylinder(wheel, c.Steel, 0.62f * d, 1.04f * w, Vector3.zero, Vector3.right, "Rim");
                AxisCylinder(wheel, c.Signal, 0.2f * d, 1.12f * w, Vector3.zero, Vector3.right, "HubCap");
                for (int k = 0; k < 18; k++)
                {
                    float a = 20f * k;
                    Quaternion rot = Quaternion.Euler(a, 0f, 0f);
                    float off = (k % 2 == 0 ? 0.2f : -0.2f) * w;
                    GameObject block = Box(wheel, c.Rubber, new Vector3(0.55f * w, 0.05f * d, 0.1f * d), rot * new Vector3(0f, 0f, 0.485f * d) + new Vector3(off, 0f, 0f), "Tread" + k);
                    block.transform.localRotation = rot * Quaternion.Euler(0f, k % 2 == 0 ? 25f : -25f, 0f);
                }
                for (int k = 0; k < 5; k++)
                {
                    Quaternion rot = Quaternion.Euler(72f * k, 0f, 0f);
                    Rod(wheel, c.Steel, new Vector3(0.55f * w, 0f, 0f) + rot * new Vector3(0f, 0f, 0.09f * d),
                        new Vector3(0.55f * w, 0f, 0f) + rot * new Vector3(0f, 0f, 0.3f * d), 0.04f * d, "Spoke" + k);
                }
                return;
            }
            // Small wheel: a thin metal drum with grousers (no tyre).
            AxisCylinder(wheel, c.Steel, d * 0.94f, w, Vector3.zero, Vector3.right, "Drum");
            AxisCylinder(wheel, c.Dark, 0.3f * d, 1.1f * w, Vector3.zero, Vector3.right, "Hub");
            for (int k = 0; k < 14; k++)
            {
                Quaternion rot = Quaternion.Euler(360f / 14f * k, 0f, 0f);
                GameObject g = Box(wheel, c.Dark, new Vector3(w, 0.06f * d, 0.03f * d), rot * new Vector3(0f, 0f, 0.48f * d), "Grouser" + k);
                g.transform.localRotation = rot;
            }
            for (int k = 0; k < 6; k++)
            {
                Quaternion rot = Quaternion.Euler(60f * k, 0f, 0f);
                Rod(wheel, c.Steel, rot * new Vector3(0f, 0f, 0.15f * d), rot * new Vector3(0f, 0f, 0.44f * d), 0.03f * d, "Spoke" + k);
            }
        }

        // ---- Stage-side pieces ------------------------------------------------------------------------------------

        /// <summary>Tail fin: a swept trapezoid with its root on the hull (−Z side of the slot) and a root fillet.</summary>
        static void TailFin(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h, RocketModel model)
        {
            bool large = m != null && m.id == ModuleCatalog.FinSideLarge;
            float t = Mathf.Clamp(0.05f * h, 0.03f, 0.12f), tip = 0.45f * t;
            float z0 = -0.5f * d, z1 = 0.5f * d;
            float tipLow = 0.05f * h, tipHigh = (large ? 0.5f : 0.6f) * h;
            var corners = new[]
            {
                new Vector3(-0.5f * t, 0f, z0), new Vector3(-0.5f * t, h, z0), new Vector3(-0.5f * tip, tipHigh, z1), new Vector3(-0.5f * tip, tipLow, z1),
                new Vector3(0.5f * t, 0f, z0), new Vector3(0.5f * t, h, z0), new Vector3(0.5f * tip, tipHigh, z1), new Vector3(0.5f * tip, tipLow, z1)
            };
            Mesh fin = Solid(corners, "Fin");
            model.OwnedMeshes.Add(fin);
            MeshPart(slot, fin, large ? c.Dark : c.White, Vector3.one, Vector3.zero, "Fin");
            Box(slot, c.Grey, new Vector3(2.5f * t, 1.02f * h, 0.06f), new Vector3(0f, 0.5f * h, z0 + 0.03f), "RootFillet");
            Rod(slot, c.Steel, new Vector3(0f, h, z0), new Vector3(0f, tipHigh, z1), 1.05f * t, "LeadingEdge");
        }

        /// <summary>
        /// Grid fin (deployed): a rectangular frame standing out from the hull with a diagonal lattice; air flows through the cells
        /// along the rocket axis. Actuator housing on the hull, hinge shaft out to the frame.
        /// </summary>
        static void GridFin(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h)
        {
            bool large = m != null && m.id == ModuleCatalog.FinTopLarge;
            Material frameMat = large ? c.Titanium : c.Grey;
            float yc = 0.5f * h, depth = Mathf.Min(0.35f * h, 0.2f * d);
            float ax = 0.45f * d, cz = 0.06f * d, az = 0.44f * d;
            float bar = 0.04f * d;
            Box(slot, frameMat, new Vector3(2f * ax, depth, bar), new Vector3(0f, yc, cz - az), "FrameIn");
            Box(slot, frameMat, new Vector3(2f * ax, depth, bar), new Vector3(0f, yc, cz + az), "FrameOut");
            Box(slot, frameMat, new Vector3(bar, depth, 2f * az), new Vector3(-ax, yc, cz), "FrameL");
            Box(slot, frameMat, new Vector3(bar, depth, 2f * az), new Vector3(ax, yc, cz), "FrameR");
            int cells = large ? 7 : 5;
            float spacing = 2f * Mathf.Max(ax, az) / cells, slat = 0.015f * d;
            for (int dirSign = -1; dirSign <= 1; dirSign += 2)
            {
                var u = new Vector3(1f, 0f, dirSign).normalized;
                var nrm = new Vector3(-dirSign, 0f, 1f).normalized;
                for (int k = -cells; k <= cells; k++)
                {
                    Vector3 o = new Vector3(0f, 0f, cz) + nrm * (k * spacing * 0.7071f);
                    if (!Clip(o, u, ax, cz, az, out float t0, out float t1) || t1 - t0 < 1e-3f) continue;
                    Vector3 mid = o + u * (0.5f * (t0 + t1));
                    GameObject s = Box(slot, frameMat, new Vector3(slat, 0.95f * depth, t1 - t0), new Vector3(mid.x, yc, mid.z), "Slat" + dirSign + "_" + k);
                    s.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(u.x, u.z) * Mathf.Rad2Deg, 0f);
                }
            }
            Box(slot, c.Dark, new Vector3(0.3f * d, 0.9f * h, 0.12f * d), new Vector3(0f, 0.5f * h, -0.5f * d + 0.06f * d), "Actuator");
            AxisCylinder(slot, c.Steel, 0.06f * d, 0.12f * d, new Vector3(0f, yc, cz - az - 0.04f * d), Vector3.forward, "Shaft");
        }

        /// <summary>Clips the line o + t·u to the rectangle |x| ≤ ax, |z − cz| ≤ az.</summary>
        static bool Clip(Vector3 o, Vector3 u, float ax, float cz, float az, out float t0, out float t1)
        {
            t0 = float.MinValue;
            t1 = float.MaxValue;
            bool Slab(float origin, float dir, float lo, float hi, ref float a, ref float b)
            {
                if (Mathf.Abs(dir) < 1e-6f) return origin >= lo && origin <= hi;
                float p = (lo - origin) / dir, q = (hi - origin) / dir;
                if (p > q) { float s = p; p = q; q = s; }
                a = Mathf.Max(a, p);
                b = Mathf.Min(b, q);
                return a <= b;
            }
            return Slab(o.x, u.x, -ax, ax, ref t0, ref t1) && Slab(o.z, u.z, cz - az, cz + az, ref t0, ref t1);
        }

        /// <summary>RCS block: a housing on the hull with small nozzles up, down and to both sides (the big one also outward).</summary>
        static void RcsBlock(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h)
        {
            bool large = m != null && m.id == ModuleCatalog.RcsLarge;
            float hx = 0.55f * d, hy = 0.6f * h, hz = 0.45f * d;
            var centre = new Vector3(0f, 0.5f * h, -0.5f * d + 0.5f * hz);
            Box(slot, large ? c.Dark : c.White, new Vector3(hx, hy, hz), centre, "Housing");
            Box(slot, c.Steel, new Vector3(0.8f * hx, 0.05f * h, 0.9f * hz), centre + new Vector3(0f, 0.5f * hy, 0f), "Cap");
            float exit = (large ? 0.24f : 0.2f) * d, len = 0.22f * Mathf.Min(d, h);
            Nozzle(slot, c.Metal, centre + new Vector3(0f, 0.5f * hy, 0.15f * hz), Vector3.up, exit, len, "NozzleUp");
            Nozzle(slot, c.Metal, centre + new Vector3(0f, -0.5f * hy, 0.15f * hz), new Vector3(0f, -1f, 0f), exit, len, "NozzleDown");
            Nozzle(slot, c.Metal, centre + new Vector3(0.5f * hx, 0f, 0.15f * hz), Vector3.right, exit, len, "NozzleRight");
            Nozzle(slot, c.Metal, centre + new Vector3(-0.5f * hx, 0f, 0.15f * hz), new Vector3(-1f, 0f, 0f), exit, len, "NozzleLeft");
            if (large) Nozzle(slot, c.Metal, centre + new Vector3(0f, 0f, 0.5f * hz), Vector3.forward, exit, len, "NozzleOut");
        }

        /// <summary>
        /// Landing leg: stowed upward along the hull, hinged at the slot bottom; <see cref="RocketModel.SetLegsDeployed"/> swings it
        /// out and down by <see cref="RocketLayout.LegDeployAngle"/>, the foot pad stays level.
        /// </summary>
        static void Leg(Transform slot, RocketPalette c, ModuleDefinition m, float d, float h, RocketModel model)
        {
            bool heavy = m != null && m.id == ModuleCatalog.LegsHeavy;
            Box(slot, c.Dark, new Vector3(0.8f * d, 0.14f * d, 0.3f * d), new Vector3(0f, 0.07f * d, -0.35f * d), "Bracket");
            AxisCylinder(slot, c.Steel, 0.12f * d, 0.9f * d, Vector3.zero, Vector3.right, "Pin");
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(slot, false);
            float len = 0.92f * h;
            if (heavy)
            {
                Box(hinge, c.Carbon, new Vector3(0.5f * d, len, 0.45f * d), new Vector3(0f, 0.5f * len, 0f), "Strut");
                Box(hinge, c.Signal, new Vector3(0.52f * d, 0.06f * len, 0.47f * d), new Vector3(0f, 0.7f * len, 0f), "Stripe");
                Box(hinge, c.Steel, new Vector3(0.2f * d, 0.6f * len, 0.2f * d), new Vector3(0f, 0.4f * len, -0.3f * d), "Damper");
            }
            else
            {
                for (int k = -1; k <= 1; k += 2)
                    Rod(hinge, c.Steel, new Vector3(k * 0.32f * d, 0f, 0f), new Vector3(0f, len, 0f), 0.12f * d, "Strut" + k);
                Rod(hinge, c.Grey, new Vector3(0f, 0.2f * len, -0.15f * d), new Vector3(0f, 0.85f * len, -0.05f * d), 0.1f * d, "Damper");
            }
            var foot = new GameObject("Foot").transform;
            foot.SetParent(hinge, false);
            foot.localPosition = new Vector3(0f, len, 0f);
            Cylinder(foot, heavy ? c.Grey : c.Steel, (heavy ? 1.1f : 0.95f) * d, 0.08f * d, 0f, "Pad");
            Ball(foot, c.Dark, 0.25f * d, new Vector3(0f, -0.05f * d, 0f), "Joint");
            var rig = new RocketModel.LegRig { Hinge = hinge, Foot = foot };
            model.Legs.Add(rig);
            RocketModel.ApplyLeg(rig);
        }

        // ---- Instruments (back −Z against the hull, face +Z outward) --------------------------------------------

        static void Instrument(Transform slot, RocketPalette c, ModuleDefinition m, float s, RocketModel model)
        {
            ModuleCapability caps = m != null ? m.capabilities : ModuleCapability.None;
            string id = m != null ? m.id : "";
            // Every instrument hangs on a bracket at the back of its slot (the hull side, −Z).
            Box(slot, c.Steel, new Vector3(0.3f * s, 0.3f * s, 0.12f * s), new Vector3(0f, 0.5f * s, -0.44f * s), "Bracket");
            if (m != null)
            {
                if (m.IsSampleCollector)
                {
                    Collector(slot, c, m, s);
                    return;
                }
                switch (m.mechanismKind)
                {
                    case MechanismKind.Drill: Drill(slot, c, m, s); return;
                    case MechanismKind.Manipulator: Arm(slot, c, s); return;
                }
                if (m.auxiliaryKind == AuxiliaryKind.Light)
                {
                    Lamp(slot, c, m, s, model);
                    return;
                }
                if (m.auxiliaryKind == AuxiliaryKind.PowerSource)
                {
                    switch (m.powerSource)
                    {
                        case PowerSourceKind.Solar: SolarWing(slot, c, m, s, model); return;
                        case PowerSourceKind.Battery: Battery(slot, c, m, s); return;
                        default:
                            if (id == ModuleCatalog.Reactor) ReactorUnit(slot, c, s, model);
                            else Rtg(slot, c, s);
                            return;
                    }
                }
                if (m.category == ModuleCategory.Communication && m.antennaRange > 0.0)
                {
                    if (m.diameter > 0.0) DishAntenna(slot, c, m, s, model);
                    else Whip(slot, c, s);
                    return;
                }
                if (m.category == ModuleCategory.Science && caps == ModuleCapability.None)
                {
                    Science(slot, c, m, s);
                    return;
                }
            }
            if ((caps & ModuleCapability.InterplanetaryNavigation) != 0)
            {
                Cylinder(slot, c.White, 0.5f * s, 0.45f * s, 0.2f * s, "Body", 0f, -0.15f * s);
                GameObject baffle = Cylinder(slot, c.Black, 0.32f * s, 0.5f * s, 0f, "Baffle");
                baffle.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
                baffle.transform.localPosition = new Vector3(0f, 0.62f * s, 0.12f * s);
                return;
            }
            if ((caps & ModuleCapability.AtmosphereAnalysis) != 0)
            {
                Cylinder(slot, c.Steel, 0.07f * s, 0.85f * s, 0f, "Mast", 0f, 0.05f * s);
                Box(slot, c.Steel, new Vector3(0.7f * s, 0.04f * s, 0.04f * s), new Vector3(0f, 0.8f * s, 0.05f * s), "CrossArm");
                for (int k = -1; k <= 1; k += 2) Ball(slot, c.White, 0.12f * s, new Vector3(k * 0.35f * s, 0.8f * s, 0.05f * s), "Cup");
                Ball(slot, c.Accent, 0.18f * s, new Vector3(0f, 0.93f * s, 0.05f * s), "Sensor");
                return;
            }
            if ((caps & ModuleCapability.SampleAnalysis) != 0)
            {
                Box(slot, c.White, new Vector3(0.7f * s, 0.55f * s, 0.55f * s), new Vector3(0f, 0.5f * s, -0.12f * s), "Body");
                GameObject lens = Cylinder(slot, c.Black, 0.22f * s, 0.35f * s, 0f, "Lens");
                lens.transform.localRotation = Quaternion.Euler(135f, 0f, 0f);
                lens.transform.localPosition = new Vector3(0f, 0.35f * s, 0.2f * s);
                Box(slot, c.Gold, new Vector3(0.72f * s, 0.06f * s, 0.57f * s), new Vector3(0f, 0.8f * s, -0.12f * s), "Radiator");
                return;
            }
            // Navigation computer and anything else: an avionics box with a status strip.
            Box(slot, c.Dark, new Vector3(0.9f * s, 0.7f * s, 0.6f * s), new Vector3(0f, 0.5f * s, -0.1f * s), "Avionics");
            Box(slot, (caps & ModuleCapability.OrbitPrediction) != 0 ? c.Accent : c.Signal, new Vector3(0.7f * s, 0.08f * s, 0.02f * s),
                new Vector3(0f, 0.62f * s, 0.21f * s), "Status");
        }

        static void Collector(Transform slot, RocketPalette c, ModuleDefinition m, float s)
        {
            bool advanced = (m.sampleKinds & SampleKinds.Rare) != 0;
            Box(slot, c.Grey, new Vector3(0.5f * s, 0.3f * s, 0.4f * s), new Vector3(0f, 0.15f * s, -0.2f * s), "Base");
            Box(slot, c.Steel, new Vector3(0.1f * s, 0.65f * s, 0.1f * s), new Vector3(0f, 0.55f * s, -0.05f * s), "UpperArm");
            Box(slot, c.Signal, new Vector3(0.16f * s, 0.16f * s, 0.16f * s), new Vector3(0f, 0.88f * s, -0.05f * s), "Elbow");
            GameObject fore = Box(slot, c.Steel, new Vector3(0.09f * s, 0.55f * s, 0.09f * s), new Vector3(0f, 0.64f * s, 0.2f * s), "ForeArm");
            fore.transform.localRotation = Quaternion.Euler(150f, 0f, 0f);
            GameObject scoop = MeshPart(slot, Dish, c.Metal, new Vector3(0.3f * s, 0.18f * s, 0.3f * s), new Vector3(0f, 0.32f * s, 0.36f * s), "Scoop");
            scoop.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            if (advanced) Cylinder(slot, c.Dark, 0.07f * s, 0.35f * s, 0.02f * s, "Drill", 0.18f * s, 0.32f * s);
        }

        /// <summary>The small science instruments: each one its own recognisable shape.</summary>
        static void Science(Transform slot, RocketPalette c, ModuleDefinition m, float s)
        {
            switch (m.id)
            {
                case ModuleCatalog.Thermometer:
                    Box(slot, c.Grey, new Vector3(0.35f * s, 0.25f * s, 0.15f * s), new Vector3(0f, 0.35f * s, -0.33f * s), "Base");
                    Rod(slot, c.Steel, new Vector3(0f, 0.4f * s, -0.28f * s), new Vector3(0f, 0.62f * s, 0.32f * s), 0.04f * s, "Boom");
                    Cylinder(slot, c.White, 0.12f * s, 0.18f * s, 0.55f * s, "Sensor", 0f, 0.34f * s);
                    for (int k = 0; k < 3; k++) Cylinder(slot, c.White, 0.22f * s, 0.015f * s, (0.58f + 0.05f * k) * s, "Louver" + k, 0f, 0.34f * s);
                    MeshPart(slot, Dome, c.Gold, new Vector3(0.12f * s, 0.06f * s, 0.12f * s), new Vector3(0f, 0.73f * s, 0.34f * s), "Cap");
                    return;
                case ModuleCatalog.Barometer:
                    Cylinder(slot, c.Steel, 0.42f * s, 0.5f * s, 0.2f * s, "Canister", 0f, -0.15f * s);
                    MeshPart(slot, Dome, c.Signal, new Vector3(0.42f * s, 0.2f * s, 0.42f * s), new Vector3(0f, 0.7f * s, -0.15f * s), "Cap");
                    Ring(slot, c.Dark, 0.44f * s, 0.05f * s, 0.32f * s, "Band");
                    Rod(slot, c.Steel, new Vector3(0f, 0.55f * s, 0.05f * s), new Vector3(0f, 0.6f * s, 0.42f * s), 0.05f * s, "Inlet");
                    Cylinder(slot, c.Dark, 0.1f * s, 0.06f * s, 0.57f * s, "Filter", 0f, 0.43f * s);
                    return;
                case ModuleCatalog.Gravimeter:
                    Cylinder(slot, c.Gold, 0.4f * s, 0.68f * s, 0.12f * s, "Dewar", 0f, -0.1f * s);
                    MeshPart(slot, Dome, c.Steel, new Vector3(0.4f * s, 0.2f * s, 0.4f * s), new Vector3(0f, 0.8f * s, -0.1f * s), "Head");
                    Ring(slot, c.Dark, 0.43f * s, 0.06f * s, 0.12f * s, "Base");
                    Box(slot, c.Dark, new Vector3(0.2f * s, 0.3f * s, 0.2f * s), new Vector3(0.3f * s, 0.25f * s, 0.05f * s), "Electronics");
                    return;
                case ModuleCatalog.Seismometer:
                    Box(slot, c.Grey, new Vector3(0.6f * s, 0.12f * s, 0.5f * s), new Vector3(0f, 0.25f * s, -0.15f * s), "Deck");
                    MeshPart(slot, Dome, c.Gold, new Vector3(0.62f * s, 0.62f * s, 0.62f * s), new Vector3(0f, 0.31f * s, 0.08f * s), "WindShield");
                    Ring(slot, c.Steel, 0.66f * s, 0.05f * s, 0.29f * s, "Skirt");
                    for (int k = 0; k < 3; k++)
                    {
                        float a = (120f * k + 60f) * Mathf.Deg2Rad;
                        Rod(slot, c.Steel, new Vector3(0.25f * s * Mathf.Sin(a), 0.32f * s, 0.08f * s + 0.25f * s * Mathf.Cos(a)),
                            new Vector3(0.36f * s * Mathf.Sin(a), 0.05f * s, 0.08f * s + 0.36f * s * Mathf.Cos(a)), 0.035f * s, "Leg" + k);
                    }
                    return;
                case ModuleCatalog.Dosimeter:
                    Box(slot, c.Grey, new Vector3(0.5f * s, 0.36f * s, 0.32f * s), new Vector3(0f, 0.5f * s, -0.2f * s), "Body");
                    for (int k = -1; k <= 1; k++)
                        AxisCylinder(slot, k == 0 ? c.Accent : c.Black, 0.11f * s, 0.18f * s, new Vector3(k * 0.15f * s, 0.55f * s, 0.02f * s), Vector3.forward, "Detector" + (k + 1));
                    Box(slot, c.Signal, new Vector3(0.5f * s, 0.05f * s, 0.01f * s), new Vector3(0f, 0.36f * s, -0.035f * s), "Label");
                    return;
                case ModuleCatalog.Magnetometer:
                    Box(slot, c.Dark, new Vector3(0.25f * s, 0.2f * s, 0.2f * s), new Vector3(0f, 0.15f * s, -0.35f * s), "Hinge");
                    Rod(slot, c.Carbon, new Vector3(0f, 0.2f * s, -0.32f * s), new Vector3(0f, 0.88f * s, 0.32f * s), 0.04f * s, "Boom");
                    Box(slot, c.White, new Vector3(0.16f * s, 0.16f * s, 0.16f * s), new Vector3(0f, 0.9f * s, 0.36f * s), "Sensor");
                    Box(slot, c.Gold, new Vector3(0.17f * s, 0.04f * s, 0.17f * s), new Vector3(0f, 0.99f * s, 0.36f * s), "Blanket");
                    return;
                case ModuleCatalog.Camera:
                    Cylinder(slot, c.Steel, 0.1f * s, 0.4f * s, 0.1f * s, "Mast", 0f, -0.2f * s);
                    Box(slot, c.White, new Vector3(0.5f * s, 0.32f * s, 0.4f * s), new Vector3(0f, 0.66f * s, -0.12f * s), "Head");
                    AxisCylinder(slot, c.Black, 0.22f * s, 0.32f * s, new Vector3(0f, 0.66f * s, 0.22f * s), Vector3.forward, "Lens");
                    AxisCylinder(slot, c.Glass, 0.16f * s, 0.02f * s, new Vector3(0f, 0.66f * s, 0.385f * s), Vector3.forward, "Glass");
                    Box(slot, c.Dark, new Vector3(0.3f * s, 0.03f * s, 0.2f * s), new Vector3(0f, 0.8f * s, 0.27f * s), "Hood");
                    Ball(slot, c.Accent, 0.05f * s, new Vector3(0.18f * s, 0.78f * s, 0.09f * s), "Led");
                    return;
            }
            Box(slot, c.White, new Vector3(0.6f * s, 0.5f * s, 0.45f * s), new Vector3(0f, 0.5f * s, -0.15f * s), "Body");
        }

        static void Lamp(Transform slot, RocketPalette c, ModuleDefinition m, float s, RocketModel model)
        {
            bool search = m.id == ModuleCatalog.LightLarge;
            Quaternion tilt = Quaternion.Euler(15f, 0f, 0f);
            Vector3 face;
            if (search)
            {
                for (int k = -1; k <= 1; k += 2) Box(slot, c.Steel, new Vector3(0.05f * s, 0.5f * s, 0.25f * s), new Vector3(k * 0.36f * s, 0.5f * s, -0.15f * s), "Yoke");
                var centre = new Vector3(0f, 0.55f * s, 0.02f * s);
                AxisCylinder(slot, c.Grey, 0.6f * s, 0.45f * s, centre, tilt * Vector3.forward, "Drum");
                face = centre + tilt * new Vector3(0f, 0f, 0.23f * s);
                AxisCylinder(slot, c.Dark, 0.66f * s, 0.06f * s, face, tilt * Vector3.forward, "Rim");
                AxisCylinder(slot, c.Lens, 0.5f * s, 0.03f * s, face + tilt * new Vector3(0f, 0f, 0.02f * s), tilt * Vector3.forward, "Lens");
                for (int k = 0; k < 4; k++) Box(slot, c.Dark, new Vector3(0.5f * s, 0.02f * s, 0.12f * s), new Vector3(0f, 0.38f * s + 0.08f * k * s, -0.25f * s), "Fin" + k);
            }
            else
            {
                GameObject body = Box(slot, c.Dark, new Vector3(0.55f * s, 0.32f * s, 0.25f * s), new Vector3(0f, 0.5f * s, -0.2f * s), "Body");
                body.transform.localRotation = tilt;
                face = new Vector3(0f, 0.5f * s, -0.2f * s) + tilt * new Vector3(0f, 0f, 0.13f * s);
                GameObject lens = Box(slot, c.Lens, new Vector3(0.45f * s, 0.22f * s, 0.02f * s), face, "Lens");
                lens.transform.localRotation = tilt;
            }
            Anchor(slot, model, face + tilt * new Vector3(0f, 0f, 0.05f * s), 15f);
        }

        /// <summary>
        /// Solar panel: a folded stack on the hull under the fairing; in free flight a wing of the real area (~285 W/m² at home)
        /// on a boom, in hinged panels.
        /// </summary>
        static void SolarWing(Transform slot, RocketPalette c, ModuleDefinition m, float s, RocketModel model)
        {
            float area = (float)Math.Max(1.0, m.powerOutput / 285.0);
            float width = Mathf.Sqrt(area / 3f), length = area / width;
            float stack = Mathf.Clamp(0.05f + 0.012f * Mathf.Sqrt(area), 0.06f, 0.35f) * s;
            GameObject stowed = Group(slot, "WingStowed", Vector3.zero, Quaternion.identity);
            Box(stowed.transform, c.Panel, new Vector3(0.85f * s, 0.9f * s, Mathf.Max(0.06f * s, stack)), new Vector3(0f, 0.5f * s, -0.25f * s), "Stack");
            Box(stowed.transform, c.Steel, new Vector3(0.9f * s, 0.04f * s, Mathf.Max(0.08f * s, stack + 0.02f)), new Vector3(0f, 0.95f * s, -0.25f * s), "Clamp");
            model.StowedWings.Add(stowed);

            GameObject deployed = Group(slot, "WingDeployed", Vector3.zero, Quaternion.identity);
            float boom = Mathf.Max(0.5f, 0.15f * length);
            Rod(deployed.transform, c.Steel, new Vector3(0f, 0.5f * s, -0.3f * s), new Vector3(0f, 0.5f * s, boom), 0.06f, "Boom");
            int panels = Mathf.Clamp(Mathf.RoundToInt(length / Mathf.Max(0.6f, width * 0.8f)), 2, 12);
            float pl = length / panels;
            for (int k = 0; k < panels; k++)
            {
                float z = boom + pl * (k + 0.5f);
                Box(deployed.transform, c.Panel, new Vector3(0.03f, width, 0.97f * pl), new Vector3(0f, 0.5f * s, z), "Panel" + k);
                Box(deployed.transform, c.Steel, new Vector3(0.04f, width, 0.02f), new Vector3(0f, 0.5f * s, z - 0.49f * pl), "Hinge" + k);
            }
            Box(deployed.transform, c.Steel, new Vector3(0.04f, 0.02f * width + 0.04f, length), new Vector3(0f, 0.5f * s + 0.5f * width, boom + 0.5f * length), "EdgeTop");
            Box(deployed.transform, c.Steel, new Vector3(0.04f, 0.02f * width + 0.04f, length), new Vector3(0f, 0.5f * s - 0.5f * width, boom + 0.5f * length), "EdgeBottom");
            model.DeployedWings.Add(deployed);
            deployed.SetActive(false);
        }

        static void Battery(Transform slot, RocketPalette c, ModuleDefinition m, float s)
        {
            bool big = m.batteryEnergy > 50 * 3.6e6;
            Box(slot, c.Grey, new Vector3(0.8f * s, 0.6f * s, 0.5f * s), new Vector3(0f, 0.45f * s, -0.15f * s), "Case");
            int n = big ? 3 : 2;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    Box(slot, c.Dark, new Vector3(0.7f / n * 0.85f * s, 0.5f / n * 0.85f * s, 0.03f * s),
                        new Vector3((-0.35f + 0.7f * (i + 0.5f) / n) * s, (0.2f + 0.5f * (j + 0.5f) / n) * s, 0.11f * s), "Cell" + i + j);
            Box(slot, c.Signal, new Vector3(0.82f * s, 0.05f * s, 0.52f * s), new Vector3(0f, 0.77f * s, -0.15f * s), "Stripe");
            for (int k = -1; k <= 1; k += 2) Cylinder(slot, k < 0 ? c.Black : c.Copper, 0.08f * s, 0.08f * s, 0.75f * s, "Terminal", k * 0.25f * s, -0.1f * s);
        }

        /// <summary>RTG: a dark finned cylinder standing on its bracket.</summary>
        static void Rtg(Transform slot, RocketPalette c, float s)
        {
            var centre = new Vector3(0f, 0f, 0.02f * s);
            Cylinder(slot, c.Dark, 0.32f * s, 0.75f * s, 0.12f * s, "Core", centre.x, centre.z);
            Cylinder(slot, c.Steel, 0.36f * s, 0.06f * s, 0.1f * s, "LowerCap", centre.x, centre.z);
            Cylinder(slot, c.Steel, 0.36f * s, 0.06f * s, 0.85f * s, "UpperCap", centre.x, centre.z);
            for (int k = 0; k < 8; k++)
            {
                Quaternion around = Quaternion.Euler(0f, 45f * k, 0f);
                GameObject fin = Box(slot, c.Radiator, new Vector3(0.02f * s, 0.7f * s, 0.18f * s), centre + around * new Vector3(0f, 0.5f * s, 0.24f * s), "Fin" + k);
                fin.transform.localRotation = around;
            }
            Ring(slot, c.Signal, 0.34f * s, 0.05f * s, 0.75f * s, "Warning");
        }

        /// <summary>Space reactor: vessel under a conical shadow shield on a boom; radiator panels fold out in free flight.</summary>
        static void ReactorUnit(Transform slot, RocketPalette c, float s, RocketModel model)
        {
            Cylinder(slot, c.Steel, 0.32f * s, 0.35f * s, 0.05f * s, "Vessel", 0f, 0.05f * s);
            Ring(slot, c.Signal, 0.34f * s, 0.04f * s, 0.25f * s, "Warning");
            MeshPart(slot, Cone, c.ShieldHeavy, new Vector3(0.6f * s, 0.25f * s, 0.6f * s), new Vector3(0f, 0.42f * s, 0.05f * s), "ShadowShield");
            Cylinder(slot, c.Dark, 0.12f * s, 0.3f * s, 0.62f * s, "Boom", 0f, 0.05f * s);
            Box(slot, c.Gold, new Vector3(0.45f * s, 0.12f * s, 0.35f * s), new Vector3(0f, 0.92f * s, -0.05f * s), "Converter");
            GameObject stowed = Group(slot, "RadiatorsStowed", Vector3.zero, Quaternion.identity);
            for (int k = -1; k <= 1; k += 2)
                Box(stowed.transform, c.Radiator, new Vector3(0.03f * s, 0.55f * s, 0.4f * s), new Vector3(k * 0.3f * s, 0.6f * s, -0.1f * s), "Radiator");
            model.StowedWings.Add(stowed);
            GameObject deployed = Group(slot, "RadiatorsDeployed", Vector3.zero, Quaternion.identity);
            for (int k = -1; k <= 1; k += 2)
            {
                Box(deployed.transform, c.Radiator, new Vector3(2.4f, 0.03f, 5f), new Vector3(k * 1.35f, 0.75f * s, 2.6f), "Radiator");
                Rod(deployed.transform, c.Steel, new Vector3(0f, 0.75f * s, 0f), new Vector3(k * 0.4f, 0.75f * s, 0.3f), 0.05f, "Arm");
            }
            model.DeployedWings.Add(deployed);
            deployed.SetActive(false);
        }

        static void Whip(Transform slot, RocketPalette c, float s)
        {
            Cylinder(slot, c.Dark, 0.18f * s, 0.12f * s, 0.05f * s, "Base", 0f, -0.25f * s);
            Cylinder(slot, c.Steel, 0.03f * s, 0.82f * s, 0.15f * s, "Mast", 0f, -0.25f * s);
            for (int k = 0; k < 4; k++)
            {
                float a = (45f + 90f * k) * Mathf.Deg2Rad;
                Rod(slot, c.Steel, new Vector3(0f, 0.9f * s, -0.25f * s), new Vector3(0.3f * s * Mathf.Sin(a), 0.82f * s, -0.25f * s + 0.3f * s * Mathf.Cos(a)), 0.02f * s, "Turnstile" + k);
            }
            Ball(slot, c.White, 0.06f * s, new Vector3(0f, 0.98f * s, -0.25f * s), "Tip");
        }

        /// <summary>A parabolic dish facing +Z: reflector, rim, feed on three struts, back hub.</summary>
        static void DishAssembly(Transform parent, RocketPalette c, float diameter, Vector3 vertex, Material reflector)
        {
            float depth = 0.18f * diameter;
            GameObject dishGo = MeshPart(parent, Dish, reflector, new Vector3(diameter, depth, diameter), vertex, "Reflector");
            dishGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // opens towards +Z
            GameObject rim = MeshPart(parent, TorusThin, c.Steel, Vector3.one * diameter, vertex + new Vector3(0f, 0f, depth), "Rim");
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float focus = diameter * diameter / (16f * depth); // f = D²/16h of a paraboloid
            Vector3 feed = vertex + new Vector3(0f, 0f, focus);
            for (int k = 0; k < 3; k++)
            {
                float a = (120f * k) * Mathf.Deg2Rad;
                Rod(parent, c.Steel, vertex + new Vector3(0.45f * diameter * Mathf.Sin(a), 0.45f * diameter * Mathf.Cos(a), 0.9f * depth), feed, 0.012f * diameter, "Strut" + k);
            }
            AxisCylinder(parent, c.Dark, 0.08f * diameter, 0.1f * diameter, feed, Vector3.forward, "Feed");
            AxisCylinder(parent, c.Dark, 0.15f * diameter, 0.12f * diameter, vertex - new Vector3(0f, 0f, 0.06f * diameter), Vector3.forward, "Hub");
        }

        /// <summary>
        /// Dishes and the relay: a small dish on a gimbal under the fairing (the big one folded); in free flight the dish of its real
        /// diameter on a boom. The relay adds a crossed dipole array.
        /// </summary>
        static void DishAntenna(Transform slot, RocketPalette c, ModuleDefinition m, float s, RocketModel model)
        {
            bool relay = m.relay;
            float real = (float)m.diameter;
            Box(slot, c.Dark, new Vector3(0.25f * s, 0.25f * s, 0.25f * s), new Vector3(0f, 0.3f * s, -0.3f * s), "Gimbal");
            GameObject stowed = Group(slot, "DishStowed", Vector3.zero, Quaternion.identity);
            if (real > 1.5f * s)
            {
                // Folded petals: a closed cone of ribs around the feed.
                MeshPart(stowed.transform, Cone, c.White, new Vector3(0.45f * s, 0.75f * s, 0.45f * s), new Vector3(0f, 0.15f * s, -0.05f * s), "Folded");
                Ring(stowed.transform, c.Steel, 0.47f * s, 0.05f * s, 0.15f * s, "Clamp");
            }
            else DishAssembly(stowed.transform, c, Mathf.Min(0.9f * s, real), new Vector3(0f, 0.5f * s, -0.15f * s), c.White);
            model.StowedWings.Add(stowed);
            GameObject deployed = Group(slot, "DishDeployed", Vector3.zero, Quaternion.identity);
            float boom = 0.3f * real;
            Rod(deployed.transform, c.Steel, new Vector3(0f, 0.5f * s, -0.3f * s), new Vector3(0f, 0.5f * s, boom), 0.04f + 0.01f * real, "Boom");
            DishAssembly(deployed.transform, c, real, new Vector3(0f, 0.5f * s, boom), relay ? c.Gold : c.White);
            model.DeployedWings.Add(deployed);
            deployed.SetActive(false);
            if (relay)
            {
                Cylinder(slot, c.Steel, 0.03f * s, 0.6f * s, 0.4f * s, "Mast", 0.3f * s, -0.2f * s);
                for (int k = 0; k < 2; k++)
                {
                    GameObject dipole = Box(slot, c.White, new Vector3(0.5f * s, 0.02f * s, 0.02f * s), new Vector3(0.3f * s, 0.95f * s, -0.2f * s), "Dipole" + k);
                    dipole.transform.localRotation = Quaternion.Euler(0f, 45f + 90f * k, 0f);
                }
            }
        }

        static void Drill(Transform slot, RocketPalette c, ModuleDefinition m, float s)
        {
            bool deep = m.drillDepth >= 1.0;
            if (deep)
            {
                // A small derrick: four posts, braces, a carriage and a long bit down the middle.
                float w = 0.14f * s;
                for (int k = 0; k < 4; k++)
                {
                    float x = (k % 2 == 0 ? 1 : -1) * w, z = (k < 2 ? 1 : -1) * w;
                    Box(slot, c.Steel, new Vector3(0.03f * s, 0.95f * s, 0.03f * s), new Vector3(x, 0.5f * s, z), "Post" + k);
                }
                for (int k = 0; k < 4; k++) Box(slot, c.Steel, new Vector3(2f * w, 0.02f * s, 2f * w), new Vector3(0f, (0.12f + 0.25f * k) * s, 0f), "Brace" + k);
                Box(slot, c.Signal, new Vector3(0.36f * s, 0.12f * s, 0.36f * s), new Vector3(0f, 0.72f * s, 0f), "Carriage");
                Cylinder(slot, c.Dark, 0.18f * s, 0.12f * s, 0.86f * s, "Motor");
                Cylinder(slot, c.Steel, 0.05f * s, 0.85f * s, 0f, "Bit");
                return;
            }
            Box(slot, c.White, new Vector3(0.42f * s, 0.42f * s, 0.38f * s), new Vector3(0f, 0.7f * s, -0.1f * s), "Housing");
            Cylinder(slot, c.Dark, 0.2f * s, 0.15f * s, 0.9f * s, "Motor", 0f, -0.1f * s);
            Cylinder(slot, c.Steel, 0.08f * s, 0.5f * s, 0f, "Bit", 0f, -0.1f * s);
            for (int k = 0; k < 8; k++)
            {
                GameObject flight = Box(slot, c.Steel, new Vector3(0.14f * s, 0.015f * s, 0.03f * s), new Vector3(0f, (0.06f + 0.05f * k) * s, -0.1f * s), "Flight" + k);
                flight.transform.localRotation = Quaternion.Euler(0f, 45f * k, 12f);
            }
        }

        /// <summary>Three-joint arm folded for launch: turret, shoulder, upper arm, elbow, forearm, wrist and a two-finger tool.</summary>
        static void Arm(Transform slot, RocketPalette c, float s)
        {
            var shoulder = new Vector3(0f, 0.18f * s, -0.3f * s);
            var elbow = new Vector3(0f, 0.85f * s, -0.18f * s);
            var wrist = new Vector3(0f, 0.32f * s, 0.18f * s);
            Cylinder(slot, c.Dark, 0.22f * s, 0.12f * s, 0.02f * s, "Turret", 0f, -0.3f * s);
            Ball(slot, c.Grey, 0.16f * s, shoulder, "Shoulder");
            Rod(slot, c.White, shoulder, elbow, 0.08f * s, "UpperArm");
            Ball(slot, c.Signal, 0.13f * s, elbow, "Elbow");
            Rod(slot, c.White, elbow, wrist, 0.07f * s, "ForeArm");
            Ball(slot, c.Grey, 0.1f * s, wrist, "Wrist");
            Box(slot, c.Dark, new Vector3(0.16f * s, 0.1f * s, 0.12f * s), wrist + new Vector3(0f, -0.08f * s, 0.06f * s), "Tool");
            for (int k = -1; k <= 1; k += 2)
                Box(slot, c.Steel, new Vector3(0.025f * s, 0.12f * s, 0.025f * s), wrist + new Vector3(k * 0.05f * s, -0.18f * s, 0.08f * s), "Finger");
        }

        // ---- Fairing ----------------------------------------------------------------------------------------------

        static void Fairing(Transform slot, RocketPalette c, RocketLayout layout, RocketModel model)
        {
            float r = (float)layout.Radius, fr = (float)layout.FairingRadius;
            float cylinder = (float)layout.FairingCylinderHeight, nose = (float)layout.FairingNoseHeight;
            float start = 0f;
            if (fr > r + 0.01f)
            {
                // Hammerhead: the payload is wider than the rocket — a conical skirt widens to the fairing.
                start = Mathf.Min(cylinder * 0.5f, (float)RocketLayout.AdapterHeight + (fr - r));
                Mesh skirt = ProceduralMeshes.Frustum(40, r, fr, false, false, "FairingSkirt");
                model.OwnedMeshes.Add(skirt);
                MeshPart(slot, skirt, c.White, new Vector3(1f, start, 1f), Vector3.zero, "Skirt");
            }
            Cylinder(slot, c.White, 2f * fr, cylinder - start, start, "Shell");
            MeshPart(slot, Ogive, c.White, new Vector3(fr, nose, fr), new Vector3(0f, cylinder, 0f), "Nose");
            for (int k = -1; k <= 1; k += 2) // clamshell seam
                Box(slot, c.Dark, new Vector3(0.03f, cylinder - start, 0.06f), new Vector3(k * fr, start + 0.5f * (cylinder - start), 0f), "Seam");
        }
    }
}
