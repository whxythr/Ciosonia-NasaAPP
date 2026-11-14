using System.Collections.Generic;
using SpaceSim.Vehicles;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// A built cabin interior: everything hangs under <see cref="Root"/> (a child of the cabin's slot, so it rides on the rocket and
    /// follows the floating origin with it). Slot frame: origin at the bottom centre of the cabin, +Y along the rocket axis, +Z the
    /// way the crew faces.
    /// </summary>
    public sealed class CockpitInteriorModel
    {
        /// <summary>One screen of the instrument panel: the live readout is drawn over this quad.</summary>
        public sealed class Screen
        {
            public Transform Transform;
            /// <summary>Quad size in its own plane [m].</summary>
            public Vector2 Size;
            public int Index;
        }

        public Transform Root;
        /// <summary>The pilot's eyes (at rest; +Z forward, +Y up).</summary>
        public Transform Eye;
        public readonly List<Screen> Screens = new List<Screen>();
        public readonly List<Light> Lamps = new List<Light>();
        internal readonly List<Material> Materials = new List<Material>();
        public CockpitInterior Kind;

        public bool Active
        {
            get => Root != null && Root.gameObject.activeSelf;
            set
            {
                if (Root != null) Root.gameObject.SetActive(value);
            }
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            foreach (Material m in Materials)
                if (m != null) Object.Destroy(m);
            Materials.Clear();
            Screens.Clear();
            Lamps.Clear();
            Root = null;
            Eye = null;
        }
    }

    /// <summary>
    /// Procedural interior of a crewed cabin for 1, 3 or 7 people (<see cref="CabinRules.InteriorOf"/>): an eight-sided hull with
    /// window openings (the real world is seen through them — there is no painted backdrop), reclined seats, the instrument panel with
    /// three screens, side consoles, an overhead panel and the cabin lamps. Sized to the cabin slot of the rocket model. Purely visual.
    /// </summary>
    public static class CockpitInteriorBuilder
    {
        const int Sides = 8;
        const float Wall = 0.04f;

        sealed class Kit
        {
            public Material Hull, Trim, Seat, SeatAccent, Console, Screen, Lamp, Metal, Red, Green, Amber;
        }

        public static CockpitInteriorModel Build(Transform slot, CockpitInterior kind, float diameter, float height, Shader lit)
        {
            var model = new CockpitInteriorModel { Kind = kind };
            model.Root = new GameObject("CockpitInterior").transform;
            model.Root.SetParent(slot, false);
            Kit k = Materials(lit, model);

            float r = Mathf.Max(0.55f, 0.5f * diameter - 0.05f); // inner apothem of the hull
            float h = Mathf.Max(2.0f, height);
            float floor = 0.12f * h, ceiling = 0.92f * h;
            int[] rows = CabinRules.SeatRows(kind);
            float pan = floor + 0.42f;
            float eye = pan + 0.78f;
            if (eye > ceiling - 0.35f) eye = ceiling - 0.35f;
            float frontZ = -0.12f * r;

            Floor(model.Root, k, r, floor, ceiling);
            Hull(model.Root, k, r, floor, ceiling, eye, kind);
            Seats(model.Root, k, rows, r, pan, frontZ);
            Panel(model, k, r, eye, frontZ);
            Overhead(model.Root, k, r, ceiling);
            Lamps(model, k, r, floor, ceiling, eye);

            var eyeGo = new GameObject("Eye").transform;
            eyeGo.SetParent(model.Root, false);
            eyeGo.localPosition = new Vector3(0f, eye, frontZ + 0.02f);
            model.Eye = eyeGo;
            model.Active = false;
            return model;
        }

        static Kit Materials(Shader lit, CockpitInteriorModel model)
        {
            Material M(Color c, float smooth, float metal = 0f)
            {
                Material m = ProceduralMeshes.LitMaterial(lit, c, smooth, metal);
                model.Materials.Add(m);
                return m;
            }
            Material Glow(Color c)
            {
                Material m = M(c, 0.6f);
                if (m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", c * 1.6f);
                }
                return m;
            }
            return new Kit
            {
                Hull = M(new Color(0.78f, 0.79f, 0.76f), 0.2f),
                Trim = M(new Color(0.32f, 0.34f, 0.37f), 0.35f, 0.3f),
                Seat = M(new Color(0.16f, 0.17f, 0.19f), 0.15f),
                SeatAccent = M(new Color(0.12f, 0.36f, 0.55f), 0.2f),
                Console = M(new Color(0.20f, 0.22f, 0.24f), 0.3f),
                Screen = Glow(new Color(0.02f, 0.07f, 0.09f)),
                Lamp = Glow(new Color(1.0f, 0.93f, 0.80f)),
                Metal = M(new Color(0.55f, 0.56f, 0.58f), 0.5f, 0.7f),
                Red = Glow(new Color(0.85f, 0.12f, 0.08f)),
                Green = Glow(new Color(0.15f, 0.80f, 0.35f)),
                Amber = Glow(new Color(0.95f, 0.62f, 0.12f)),
            };
        }

        static GameObject Box(Transform parent, Material m, Vector3 size, Vector3 centre, Quaternion rotation, string name, bool shadows = true)
        {
            GameObject g = ProceduralMeshes.Primitive(PrimitiveType.Cube, name, parent, m);
            g.transform.localScale = size;
            g.transform.localPosition = centre;
            g.transform.localRotation = rotation;
            MeshRenderer mr = g.GetComponent<MeshRenderer>();
            if (mr != null) mr.shadowCastingMode = shadows ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            return g;
        }

        static void Floor(Transform root, Kit k, float r, float floor, float ceiling)
        {
            float d = 2f * r / Mathf.Cos(Mathf.PI / Sides) + 0.1f;
            GameObject f = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Floor", root, k.Trim);
            f.transform.localScale = new Vector3(d, 0.03f, d);
            f.transform.localPosition = new Vector3(0f, floor - 0.03f, 0f);
            f.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
            GameObject c = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Ceiling", root, k.Hull);
            c.transform.localScale = new Vector3(d, 0.03f, d);
            c.transform.localPosition = new Vector3(0f, ceiling + 0.03f, 0f);
            c.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
            // A docking hatch in the ceiling (closed).
            Box(root, k.Metal, new Vector3(0.62f, 0.05f, 0.62f), new Vector3(0f, ceiling, -0.1f * r), Quaternion.identity, "Hatch", false);
        }

        /// <summary>Which hull faces (0 = straight ahead, then every 45° clockwise seen from above) have a window.</summary>
        static bool HasWindow(int face, CockpitInterior kind)
        {
            switch (face)
            {
                case 0:
                case 1:
                case 7: return true;
                case 2:
                case 6: return kind != CockpitInterior.Single;
                default: return false;
            }
        }

        static void Hull(Transform root, Kit k, float r, float floor, float ceiling, float eye, CockpitInterior kind)
        {
            float side = 2f * r * Mathf.Tan(Mathf.PI / Sides) + 0.02f;
            float sill = eye - 0.28f, head = eye + 0.34f;
            float opening = 0.62f * side;
            for (int i = 0; i < Sides; i++)
            {
                float angle = 360f * i / Sides;
                Quaternion q = Quaternion.Euler(0f, angle, 0f);
                var face = new GameObject("Hull" + i).transform;
                face.SetParent(root, false);
                face.localRotation = q;
                face.localPosition = Vector3.zero;
                float z = r + 0.5f * Wall;
                if (!HasWindow(i, kind))
                {
                    Box(face, k.Hull, new Vector3(side, ceiling - floor, Wall), new Vector3(0f, 0.5f * (floor + ceiling), z), Quaternion.identity, "Wall");
                    // Stowage lockers on the blind faces.
                    Box(face, k.Trim, new Vector3(0.7f * side, 0.45f, 0.18f), new Vector3(0f, floor + 0.9f, r - 0.09f), Quaternion.identity, "Locker");
                    continue;
                }
                // Below the sill, above the head, and the two jambs: a frame round the opening.
                Box(face, k.Hull, new Vector3(side, sill - floor, Wall), new Vector3(0f, 0.5f * (floor + sill), z), Quaternion.identity, "Lower");
                Box(face, k.Hull, new Vector3(side, ceiling - head, Wall), new Vector3(0f, 0.5f * (head + ceiling), z), Quaternion.identity, "Upper");
                float jamb = 0.5f * (side - opening);
                float jx = 0.5f * opening + 0.5f * jamb;
                Box(face, k.Hull, new Vector3(jamb, head - sill, Wall), new Vector3(-jx, 0.5f * (sill + head), z), Quaternion.identity, "JambL");
                Box(face, k.Hull, new Vector3(jamb, head - sill, Wall), new Vector3(jx, 0.5f * (sill + head), z), Quaternion.identity, "JambR");
                // A thick metal frame (the window is a thick multi-pane port), slightly inside the hull.
                float t = 0.035f, depth = 0.09f, zi = r - 0.5f * depth;
                Box(face, k.Metal, new Vector3(opening + 2f * t, t, depth), new Vector3(0f, sill - 0.5f * t, zi), Quaternion.identity, "FrameB", false);
                Box(face, k.Metal, new Vector3(opening + 2f * t, t, depth), new Vector3(0f, head + 0.5f * t, zi), Quaternion.identity, "FrameT", false);
                Box(face, k.Metal, new Vector3(t, head - sill, depth), new Vector3(-0.5f * opening - 0.5f * t, 0.5f * (sill + head), zi), Quaternion.identity, "FrameL", false);
                Box(face, k.Metal, new Vector3(t, head - sill, depth), new Vector3(0.5f * opening + 0.5f * t, 0.5f * (sill + head), zi), Quaternion.identity, "FrameR", false);
            }
            // Ribs between the faces.
            for (int i = 0; i < Sides; i++)
            {
                float angle = 360f * (i + 0.5f) / Sides;
                Quaternion q = Quaternion.Euler(0f, angle, 0f);
                float rr = r / Mathf.Cos(Mathf.PI / Sides) - 0.04f;
                Vector3 p = q * new Vector3(0f, 0.5f * (floor + ceiling), rr);
                Box(root, k.Trim, new Vector3(0.06f, ceiling - floor, 0.08f), p, q, "Rib" + i, false);
            }
        }

        static void Seats(Transform root, Kit k, int[] rows, float r, float pan, float frontZ)
        {
            for (int row = 0; row < rows.Length; row++)
            {
                int n = rows[row];
                float usable = 1.7f * r;
                float width = Mathf.Min(0.56f, usable / Mathf.Max(1, n));
                float z = frontZ - row * 0.62f * r;
                float y = pan + row * 0.55f; // the back row stands on a raised tier
                if (row > 0)
                    Box(root, k.Trim, new Vector3(2f * r * 0.9f, y - pan + 0.05f, 0.6f * r), new Vector3(0f, 0.5f * (pan - 0.42f + y - 0.08f), z), Quaternion.identity, "Tier");
                for (int i = 0; i < n; i++)
                {
                    float x = (i - 0.5f * (n - 1)) * width;
                    // Outer seats turn slightly inward, like a fan, so everyone faces the panel.
                    float yaw = n > 1 ? -12f * (i - 0.5f * (n - 1)) / Mathf.Max(1f, 0.5f * (n - 1)) : 0f;
                    var seat = new GameObject("Seat" + row + "_" + i).transform;
                    seat.SetParent(root, false);
                    seat.localPosition = new Vector3(x, y, z);
                    seat.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    float w = width * 0.88f;
                    Box(seat, k.Seat, new Vector3(w, 0.1f, 0.52f), new Vector3(0f, 0f, 0.02f), Quaternion.Euler(-6f, 0f, 0f), "Pan");
                    Box(seat, k.Seat, new Vector3(w, 0.82f, 0.1f), new Vector3(0f, 0.42f, -0.27f), Quaternion.Euler(-14f, 0f, 0f), "Back");
                    Box(seat, k.SeatAccent, new Vector3(w * 0.9f, 0.5f, 0.02f), new Vector3(0f, 0.42f, -0.215f), Quaternion.Euler(-14f, 0f, 0f), "Cushion", false);
                    Box(seat, k.Seat, new Vector3(w * 0.6f, 0.2f, 0.14f), new Vector3(0f, 0.92f, -0.37f), Quaternion.Euler(-14f, 0f, 0f), "Headrest");
                    Box(seat, k.Trim, new Vector3(0.06f, 0.08f, 0.42f), new Vector3(-0.5f * w, 0.2f, 0f), Quaternion.identity, "ArmL", false);
                    Box(seat, k.Trim, new Vector3(0.06f, 0.08f, 0.42f), new Vector3(0.5f * w, 0.2f, 0f), Quaternion.identity, "ArmR", false);
                    Box(seat, k.Metal, new Vector3(0.08f, 0.38f, 0.08f), new Vector3(0f, -0.24f, 0f), Quaternion.identity, "Strut", false);
                    // Harness straps.
                    Box(seat, k.Amber, new Vector3(0.04f, 0.5f, 0.012f), new Vector3(-0.12f * w, 0.5f, -0.205f), Quaternion.Euler(-14f, 0f, 0f), "StrapL", false);
                    Box(seat, k.Amber, new Vector3(0.04f, 0.5f, 0.012f), new Vector3(0.12f * w, 0.5f, -0.205f), Quaternion.Euler(-14f, 0f, 0f), "StrapR", false);
                    if (row == 0 && i == (n - 1) / 2)
                    {
                        // The commander's hand controller on the right armrest.
                        Box(seat, k.Console, new Vector3(0.09f, 0.06f, 0.12f), new Vector3(0.5f * w, 0.27f, 0.16f), Quaternion.identity, "StickBase", false);
                        GameObject stick = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Stick", seat, k.Seat);
                        stick.transform.localScale = new Vector3(0.035f, 0.07f, 0.035f);
                        stick.transform.localPosition = new Vector3(0.5f * w, 0.36f, 0.16f);
                        stick.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    }
                }
            }
        }

        static void Panel(CockpitInteriorModel model, Kit k, float r, float eye, float frontZ)
        {
            Transform root = model.Root;
            float width = Mathf.Min(1.5f, 1.55f * r);
            float z = frontZ + 0.66f;
            if (z > r - 0.18f) z = r - 0.18f;
            float y = eye - 0.42f;
            var console = new GameObject("Panel").transform;
            console.SetParent(root, false);
            console.localPosition = new Vector3(0f, y, z);
            console.localRotation = Quaternion.Euler(32f, 0f, 0f); // top leans away: the face looks up at the crew
            Box(console, k.Console, new Vector3(width, 0.36f, 0.12f), Vector3.zero, Quaternion.identity, "Board");
            // Support from the floor.
            Box(root, k.Trim, new Vector3(0.14f, y - 0.1f, 0.14f), new Vector3(0f, 0.5f * (y - 0.1f) + 0.05f, z + 0.05f), Quaternion.identity, "Pedestal", false);

            float screenW = width * 0.29f, screenH = 0.22f;
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * width * 0.325f;
                GameObject q = ProceduralMeshes.Primitive(PrimitiveType.Quad, "Screen" + i, console, k.Screen);
                q.transform.localScale = new Vector3(screenW, screenH, 1f);
                q.transform.localPosition = new Vector3(x, 0.02f, -0.061f);
                q.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                Box(console, k.Trim, new Vector3(screenW + 0.03f, screenH + 0.03f, 0.01f), new Vector3(x, 0.02f, -0.057f), Quaternion.identity, "Bezel" + i, false);
                model.Screens.Add(new CockpitInteriorModel.Screen { Transform = q.transform, Size = new Vector2(screenW, screenH), Index = i });
            }
            // A row of switches and warning lamps under the screens.
            Material[] lamps = { k.Green, k.Amber, k.Green, k.Red, k.Green, k.Amber, k.Green };
            for (int i = 0; i < lamps.Length; i++)
            {
                float x = (i - 0.5f * (lamps.Length - 1)) * width * 0.12f;
                Box(console, lamps[i], new Vector3(0.025f, 0.018f, 0.012f), new Vector3(x, -0.14f, -0.064f), Quaternion.identity, "Indicator" + i, false);
            }
            // Side consoles along the walls left and right of the crew.
            for (int s = -1; s <= 1; s += 2)
            {
                var side = new GameObject(s < 0 ? "ConsoleL" : "ConsoleR").transform;
                side.SetParent(root, false);
                side.localPosition = new Vector3(s * (0.82f * r), eye - 0.62f, frontZ + 0.1f);
                side.localRotation = Quaternion.Euler(25f, s * 70f, 0f); // turned towards the seats, tilted up
                Box(side, k.Console, new Vector3(0.42f, 0.24f, 0.1f), Vector3.zero, Quaternion.identity, "Board", false);
                for (int b = 0; b < 6; b++)
                {
                    Material m = b % 3 == 0 ? k.Amber : b % 3 == 1 ? k.Trim : k.Green;
                    Box(side, m, new Vector3(0.04f, 0.03f, 0.02f), new Vector3(-0.14f + (b % 3) * 0.14f, b < 3 ? 0.05f : -0.05f, -0.055f), Quaternion.identity, "Key" + b, false);
                }
            }
        }

        static void Overhead(Transform root, Kit k, float r, float ceiling)
        {
            var panel = new GameObject("OverheadPanel").transform;
            panel.SetParent(root, false);
            panel.localPosition = new Vector3(0f, ceiling - 0.12f, 0.35f * r);
            panel.localRotation = Quaternion.Euler(-55f, 0f, 0f); // faces down and back at the crew
            Box(panel, k.Console, new Vector3(0.9f * r, 0.3f, 0.06f), Vector3.zero, Quaternion.identity, "Board", false);
            for (int i = 0; i < 8; i++)
            {
                Material m = i == 3 ? k.Red : i % 2 == 0 ? k.Green : k.Trim;
                Box(panel, m, new Vector3(0.035f, 0.035f, 0.02f), new Vector3((i % 4 - 1.5f) * 0.18f * r, i < 4 ? 0.07f : -0.07f, -0.035f), Quaternion.identity, "Switch" + i, false);
            }
        }

        static void Lamps(CockpitInteriorModel model, Kit k, float r, float floor, float ceiling, float eye)
        {
            // Two warm flood lamps in the ceiling and a cool glow from the panel. No shadows: the sun through the windows casts them.
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("CabinLamp" + i);
                go.transform.SetParent(model.Root, false);
                go.transform.localPosition = new Vector3(0f, ceiling - 0.08f, (i == 0 ? 0.3f : -0.45f) * r);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = Mathf.Max(2.5f, 2.4f * r + (ceiling - floor));
                l.intensity = 1.6f;
                l.color = new Color(1f, 0.92f, 0.80f);
                l.shadows = LightShadows.None;
                model.Lamps.Add(l);
                GameObject fit = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Fitting", go.transform, k.Lamp);
                fit.transform.localScale = new Vector3(0.22f, 0.012f, 0.22f);
                fit.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                fit.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            var glow = new GameObject("PanelGlow");
            glow.transform.SetParent(model.Root, false);
            glow.transform.localPosition = new Vector3(0f, eye - 0.25f, 0.25f * r);
            Light g = glow.AddComponent<Light>();
            g.type = LightType.Point;
            g.range = 1.6f;
            g.intensity = 0.35f;
            g.color = new Color(0.55f, 0.85f, 1f);
            g.shadows = LightShadows.None;
            model.Lamps.Add(g);
        }
    }
}
