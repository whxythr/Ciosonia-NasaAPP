using System.Collections.Generic;
using SpaceSim.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The spaceport's assembly hangar: a tall building <see cref="DistanceFromPad"/> m south of the launch pad, on the same flattened
    /// ground, its gate facing the pad. Built from primitives (or from the hangar model in <see cref="ModuleVisualLibrary"/>); the
    /// ceiling spotlights are on only while the rocket is inside. Child of the <see cref="LaunchSite"/>, so it moves with the planet
    /// exactly like the pad. Local frame: origin = floor centre on the ground, +X east, +Y up, +Z north (towards the gate and the pad).
    /// Purely visual — nothing here touches physics.
    /// </summary>
    public sealed class HangarBuilding : MonoBehaviour
    {
        public const float DistanceFromPad = 150f;
        /// <summary>Floor slab top above the physical ground — raised like the pad deck, never coplanar with the terrain mesh.</summary>
        public const float FloorHeight = LaunchSite.DeckHeight;
        public const float InteriorWidth = 90f, InteriorDepth = 80f, InteriorHeight = 125f;
        public const float Wall = 1.5f;
        public const float GateWidth = 32f, GateHeight = 115f;
        /// <summary>The rocket stands on an assembly stand this tall in the middle of the floor.</summary>
        public const float StandHeight = 0.6f;
        const float LightMargin = 3f;

        [Tooltip("Intensity of each of the four ceiling spotlights (URP: inverse-square falloff, ~90 m to the floor).")]
        [SerializeField] float spotIntensity = 6000f;

        LaunchSite site;
        Vector3 offsetInSite;
        Transform bay;
        readonly List<Material> materials = new List<Material>();
        readonly List<Light> lights = new List<Light>();
        readonly List<GameObject> fixtures = new List<GameObject>();
        Material lampOn, lampOff;

        /// <summary>The rocket stands here (bottom of its lowest stage on the stand).</summary>
        public Transform Bay => bay;
        public static Vector3 BayLocal => new Vector3(0f, FloorHeight + StandHeight, 0f);
        public bool LightsOn { get; private set; }
        public IReadOnlyList<Light> Lights => lights;

        public float SpotIntensity
        {
            get => spotIntensity;
            set
            {
                spotIntensity = Mathf.Max(0f, value);
                foreach (Light l in lights) l.intensity = spotIntensity;
            }
        }

        public void Initialize(LaunchSite launchSite, Shader lit, ModuleVisualLibrary library)
        {
            site = launchSite;
            offsetInSite = new Vector3(0f, 0f, -DistanceFromPad);
            transform.localPosition = offsetInSite;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            if (library != null && library.hangarModel != null)
            {
                GameObject custom = Instantiate(library.hangarModel, transform);
                custom.name = library.hangarModel.name;
                custom.transform.localPosition = Vector3.zero;
                foreach (Collider c in custom.GetComponentsInChildren<Collider>()) Destroy(c);
                BuildStand(lit);
            }
            else BuildGeometry(lit);
            BuildLights(lit);

            bay = new GameObject("Bay").transform;
            bay.SetParent(transform, false);
            bay.localPosition = BayLocal;
            SetLights(false);
        }

        void OnDestroy()
        {
            foreach (Material m in materials)
                if (m != null) Destroy(m);
        }

        /// <summary>Ceiling spotlights and lamp glow on/off (on only while the rocket is in, so they never light the pad).</summary>
        public void SetLights(bool on)
        {
            LightsOn = on;
            foreach (Light l in lights) l.enabled = on;
            foreach (GameObject f in fixtures)
            {
                var r = f.GetComponent<MeshRenderer>();
                if (r != null) r.sharedMaterial = on ? lampOn : lampOff;
            }
        }

        // ---- Frames -----------------------------------------------------------------------------------------

        /// <summary>A point of this building's local frame [m] → simulation (inertial) frame at <paramref name="time"/>, in double precision.</summary>
        public Vector3d LocalToSimulation(Vector3 local, double time)
        {
            PlanetFixedObject anchor = site.Anchor;
            Vector3 inSite = offsetInSite + local; // the building is not rotated or scaled inside the site
            Vector3d fixedPosition = anchor.FixedPosition + anchor.LocalToFixed.Rotate(CoordinateSystem.FromUnityVector(inSite));
            return site.Planet.Model.ToInertial(fixedPosition, time);
        }

        /// <summary>A direction of this building's local frame → simulation (inertial) frame at <paramref name="time"/>.</summary>
        public Vector3d DirectionToSimulation(Vector3 local, double time)
        {
            Vector3d fixedDirection = site.Anchor.LocalToFixed.Rotate(CoordinateSystem.FromUnityVector(local));
            return site.Planet.Model.Rotation.FixedToInertial(fixedDirection, time);
        }

        /// <summary>Keeps a point of the local frame inside the hall, <paramref name="margin"/> m from the walls, floor and roof.</summary>
        public static Vector3 ClampInside(Vector3 local, float margin)
        {
            float hx = 0.5f * InteriorWidth - margin, hz = 0.5f * InteriorDepth - margin;
            return new Vector3(Mathf.Clamp(local.x, -hx, hx), Mathf.Clamp(local.y, FloorHeight + margin, FloorHeight + InteriorHeight - margin),
                Mathf.Clamp(local.z, -hz, hz));
        }

        // ---- Geometry ---------------------------------------------------------------------------------------

        Material Make(Shader lit, Color color, float smoothness = 0.2f, float metallic = 0f)
        {
            Material m = ProceduralMeshes.LitMaterial(lit, color, smoothness, metallic);
            materials.Add(m);
            return m;
        }

        GameObject Box(Material m, Vector3 size, Vector3 centre, string name)
        {
            GameObject g = ProceduralMeshes.Primitive(PrimitiveType.Cube, name, transform, m);
            g.transform.localScale = size;
            g.transform.localPosition = centre;
            return g;
        }

        void BuildStand(Shader lit)
        {
            Material steel = Make(lit, new Color(0.26f, 0.27f, 0.29f), 0.35f, 0.4f);
            Material marker = Make(lit, new Color(0.92f, 0.78f, 0.15f), 0.25f);
            GameObject stand = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "AssemblyStand", transform, steel);
            stand.transform.localScale = new Vector3(14f, 0.5f * StandHeight, 14f);
            stand.transform.localPosition = new Vector3(0f, FloorHeight + 0.5f * StandHeight, 0f);
            GameObject ring = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "StandMarker", transform, marker);
            ring.transform.localScale = new Vector3(15f, 0.01f, 15f);
            ring.transform.localPosition = new Vector3(0f, FloorHeight + 0.01f, 0f);
        }

        void BuildGeometry(Shader lit)
        {
            Material concrete = Make(lit, new Color(0.55f, 0.55f, 0.53f), 0.15f);
            Material floor = Make(lit, new Color(0.42f, 0.43f, 0.44f), 0.35f);
            Material cladding = Make(lit, new Color(0.80f, 0.81f, 0.82f), 0.25f);
            Material trim = Make(lit, new Color(0.10f, 0.18f, 0.34f), 0.3f);
            Material door = Make(lit, new Color(0.66f, 0.68f, 0.70f), 0.3f, 0.2f);
            Material inner = Make(lit, new Color(0.60f, 0.62f, 0.64f), 0.2f);
            Material paint = Make(lit, new Color(0.78f, 0.26f, 0.10f), 0.3f);
            Material steel = Make(lit, new Color(0.30f, 0.31f, 0.33f), 0.35f, 0.4f);
            Material marker = Make(lit, new Color(0.92f, 0.78f, 0.15f), 0.25f);

            float W = InteriorWidth, D = InteriorDepth, H = InteriorHeight, F = FloorHeight, T = Wall;
            float outerW = W + 2f * T, outerD = D + 2f * T;

            // Foundation slab (sunk into the ground) and the polished hall floor.
            Box(concrete, new Vector3(outerW + 6f, 3f, outerD + 6f), new Vector3(0f, F - 1.5f - 0.02f, 0f), "Foundation");
            Box(floor, new Vector3(W, 0.04f, D), new Vector3(0f, F - 0.01f, 0f), "HallFloor");

            // Walls: back (south), east, west; the north wall frames the gate. Thick enough that the sun never leaks in.
            // From 3 m below the floor (into the foundation) up to the top of the roof slab.
            float wallHeight = H + T + 3f, wallY = F - 3f + 0.5f * wallHeight;
            Box(cladding, new Vector3(outerW, wallHeight, T), new Vector3(0f, wallY, -0.5f * D - 0.5f * T), "WallSouth");
            Box(cladding, new Vector3(T, wallHeight, outerD), new Vector3(0.5f * W + 0.5f * T, wallY, 0f), "WallEast");
            Box(cladding, new Vector3(T, wallHeight, outerD), new Vector3(-0.5f * W - 0.5f * T, wallY, 0f), "WallWest");
            float pillar = 0.5f * (outerW - GateWidth);
            float zNorth = 0.5f * D + 0.5f * T;
            for (int s = -1; s <= 1; s += 2)
                Box(cladding, new Vector3(pillar, wallHeight, T), new Vector3(s * (0.5f * GateWidth + 0.5f * pillar), wallY, zNorth), "GatePillar");
            Box(cladding, new Vector3(GateWidth, H - GateHeight, T), new Vector3(0f, F + GateHeight + 0.5f * (H - GateHeight), zNorth), "GateLintel");
            Box(cladding, new Vector3(outerW, T, outerD), new Vector3(0f, F + H + 0.5f * T, 0f), "Roof");

            // Inner lining (lighter, slightly in front of the walls) so the hall reads as an interior, not the outside of a box.
            Box(inner, new Vector3(W, H, 0.1f), new Vector3(0f, F + 0.5f * H, -0.5f * D + 0.05f), "LiningSouth");
            Box(inner, new Vector3(0.1f, H, D), new Vector3(0.5f * W - 0.05f, F + 0.5f * H, 0f), "LiningEast");
            Box(inner, new Vector3(0.1f, H, D), new Vector3(-0.5f * W + 0.05f, F + 0.5f * H, 0f), "LiningWest");

            // Gate: two sliding leaves outside the north wall, closed while the rocket is assembled.
            float leaf = 0.5f * GateWidth + 0.75f;
            for (int s = -1; s <= 1; s += 2)
            {
                Box(door, new Vector3(leaf, GateHeight + 1f, 1.2f), new Vector3(s * 0.5f * leaf, F + 0.5f * (GateHeight + 1f), 0.5f * D + T + 0.6f), "GateLeaf");
                for (int k = 1; k < 12; k++)
                    Box(trim, new Vector3(leaf, 0.35f, 0.2f), new Vector3(s * 0.5f * leaf, F + k * GateHeight / 12f, 0.5f * D + T + 1.3f), "GateRib");
            }
            Box(trim, new Vector3(GateWidth + 4f, 2f, 1.6f), new Vector3(0f, F + GateHeight + 1.8f, 0.5f * D + T + 0.6f), "GateHeader");

            // Exterior: a dark band under the roof edge and vertical ribs every 10 m (scale against the sky).
            for (int s = -1; s <= 1; s += 2)
            {
                Box(trim, new Vector3(outerW + 0.8f, 6f, 0.4f), new Vector3(0f, F + H - 1f, s * (0.5f * outerD + 0.2f)), "RoofBand");
                Box(trim, new Vector3(0.4f, 6f, outerD + 0.8f), new Vector3(s * (0.5f * outerW + 0.2f), F + H - 1f, 0f), "RoofBand");
            }
            for (float x = -0.5f * W + 5f; x < 0.5f * W; x += 10f)
                Box(trim, new Vector3(0.6f, H, 0.4f), new Vector3(x, F + 0.5f * H, -0.5f * D - T - 0.2f), "RibSouth");
            for (float z = -0.5f * D + 5f; z < 0.5f * D; z += 10f)
                for (int s = -1; s <= 1; s += 2)
                    Box(trim, new Vector3(0.4f, H, 0.6f), new Vector3(s * (0.5f * W + T + 0.2f), F + 0.5f * H, z), "RibSide");

            // Work platforms on the side walls every 14 m, with railings — scale for the rocket and the camera.
            for (int level = 1; level <= 8; level++)
            {
                float y = F + 14f * level;
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = s * (0.5f * W - 3f);
                    Box(paint, new Vector3(6f, 0.6f, 18f), new Vector3(x, y, 0f), "Platform");
                    Box(steel, new Vector3(0.08f, 1.1f, 18f), new Vector3(x - s * 3f, y + 0.85f, 0f), "Railing");
                    for (int p = -1; p <= 1; p++)
                        Box(steel, new Vector3(0.12f, 1.1f, 0.12f), new Vector3(x - s * 3f, y + 0.55f, p * 8.8f), "Post");
                }
            }
            // Stairs tower in the south-east corner.
            Box(steel, new Vector3(5f, H - 4f, 5f), new Vector3(0.5f * W - 4f, F + 0.5f * (H - 4f), -0.5f * D + 4f), "StairTower");

            // Overhead bridge crane with its trolley and hook.
            float craneY = F + H - 7f;
            for (int s = -1; s <= 1; s += 2)
                Box(steel, new Vector3(W, 1.2f, 1.2f), new Vector3(0f, craneY + 2.5f, s * 0.45f * D), "CraneRail");
            Box(marker, new Vector3(W - 1f, 2.6f, 3f), new Vector3(0f, craneY, -8f), "CraneBridge");
            Box(steel, new Vector3(4f, 2f, 4f), new Vector3(0f, craneY - 2.2f, -8f), "CraneTrolley");
            Box(steel, new Vector3(0.12f, 18f, 0.12f), new Vector3(0f, craneY - 12f, -8f), "CraneCable");
            Box(marker, new Vector3(1.4f, 1.6f, 1.4f), new Vector3(0f, craneY - 21.5f, -8f), "CraneHook");

            // Floor: safety lines around the bay and a lane to the gate.
            for (int s = -1; s <= 1; s += 2)
            {
                Box(marker, new Vector3(0.3f, 0.02f, D - 6f), new Vector3(s * 12f, F + 0.012f, 0f), "LaneLine");
                Box(marker, new Vector3(24f, 0.02f, 0.3f), new Vector3(0f, F + 0.012f, s * 12f), "BayLine");
            }
            // Equipment along the walls.
            for (int k = 0; k < 4; k++)
            {
                Box(steel, new Vector3(4f, 2.4f, 2.2f), new Vector3(-0.5f * W + 4f, F + 1.2f, -20f + k * 9f), "Rack");
                Box(trim, new Vector3(2.4f, 1.2f, 3.6f), new Vector3(0.5f * W - 6f, F + 0.6f, 6f + k * 5f), "Container");
            }

            // Crawlerway: a concrete lane from the gate to the pad apron (150 m north).
            float laneStart = 0.5f * D + T, laneEnd = DistanceFromPad - 46f;
            Box(concrete, new Vector3(30f, 2f, laneEnd - laneStart), new Vector3(0f, 0.6f - 1f, 0.5f * (laneStart + laneEnd)), "Crawlerway");
            for (int s = -1; s <= 1; s += 2)
                Box(marker, new Vector3(0.4f, 0.02f, laneEnd - laneStart), new Vector3(s * 13f, 0.61f, 0.5f * (laneStart + laneEnd)), "CrawlerLine");

            BuildStand(lit);
        }

        void BuildLights(Shader lit)
        {
            lampOff = Make(lit, new Color(0.55f, 0.56f, 0.58f), 0.4f);
            lampOn = Make(lit, new Color(1f, 0.98f, 0.92f), 0.4f);
            if (lampOn.HasProperty("_EmissionColor"))
            {
                lampOn.EnableKeyword("_EMISSION");
                lampOn.SetColor("_EmissionColor", new Color(6f, 5.8f, 5.2f));
            }
            float y = FloorHeight + InteriorHeight - LightMargin;
            Vector3 target = new Vector3(0f, FloorHeight + 28f, 0f);
            int index = 0;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var position = new Vector3(sx * 30f, y, sz * 26f);
                    var go = new GameObject("Spotlight" + index);
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = position;
                    go.transform.localRotation = Quaternion.LookRotation(target - position, Vector3.up);
                    Light l = go.AddComponent<Light>();
                    l.type = LightType.Spot;
                    l.range = 240f;
                    l.spotAngle = 82f;
                    l.innerSpotAngle = 50f;
                    l.intensity = spotIntensity;
                    l.color = new Color(1f, 0.97f, 0.90f);
                    // Two diagonal lights cast shadows (the rocket's shadow on the floor); URP's additional-light shadow atlas holds them.
                    l.shadows = index == 0 || index == 3 ? LightShadows.Soft : LightShadows.None;
                    l.shadowBias = 0.05f;
                    l.shadowNormalBias = 0.4f;
                    lights.Add(l);
                    GameObject lamp = Box(lampOff, new Vector3(4f, 0.5f, 4f), position + new Vector3(0f, 1.2f, 0f), "Lamp" + index);
                    lamp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    fixtures.Add(lamp);
                    index++;
                }
            }
            // Rows of ceiling fixtures (glow only) between the spotlights.
            for (int k = -2; k <= 2; k++)
            {
                GameObject strip = Box(lampOff, new Vector3(1.2f, 0.3f, 40f), new Vector3(k * 12f, FloorHeight + InteriorHeight - 0.8f, 0f), "LampStrip");
                strip.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                fixtures.Add(strip);
            }
        }
    }
}
