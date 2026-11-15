using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpaceSim.Flight;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Navigation;
using SpaceSim.Physics;
using SpaceSim.Procedural;
using SpaceSim.Units;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The full-screen 3D map of the star system: planets as lit spheres with their real surface colours and rotation, orbits, the
    /// predicted trajectory, periapsis and apoapsis. It draws with its own camera while the flight camera is switched off; the
    /// flight itself keeps running. Display only: positions come from <see cref="PatchedConicSystem"/> and
    /// <see cref="NavigationService"/>.
    ///
    /// Space: everything is expressed relative to the point the map looks at and scaled so the camera stays
    /// <see cref="CameraDistance"/> units away from it, which keeps float precision from low orbit out to the whole system.
    /// </summary>
    [DefaultExecutionOrder(205)]
    public sealed class OrbitalMapView : MonoBehaviour
    {
        const int MapLayer = 31;
        const float CameraDistance = 100f, NearPlane = 0.05f, FarPlane = 2e6f, FarLimit = 1e6f, FieldOfView = 50f;
        const float RotateSpeed = 0.15f, ZoomPerNotch = 0.2f, RotationSmoothing = 14f, ZoomSmoothing = 10f, FocusSmoothing = 7f;
        const int FocusVehicle = -2;
        const int OrbitSegments = 720;
        const double VehicleMinDistance = 2e4;
        /// <summary>A polyline is hidden once the view is this much smaller than the curve: its chords would visibly miss what lies on it.</summary>
        const double MaxCurveToView = 300.0;
        const int SurfaceLevel = 2, SurfaceResolution = 32;
        const float ApsisMinPixels = 24f, LabelClearance = 40f;

        static readonly int StarId = Shader.PropertyToID("_SpaceSimMapStar");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int WidthId = Shader.PropertyToID("_Width");
        static readonly int EmissiveId = Shader.PropertyToID("_Emissive");
        static readonly int RimId = Shader.PropertyToID("_Rim");

        static readonly Color OrbitColor = new Color(0.55f, 0.68f, 0.82f, 1f);
        static readonly Color LabelColor = new Color(0.86f, 0.9f, 0.95f, 1f);

        sealed class MapBody
        {
            public int Index;
            public string Name;
            public double Radius;
            public float MinPixels;
            public PlanetRuntime Runtime;
            public Transform Transform;
            public MeshRenderer Renderer;
            public MeshFilter Filter;
            public MapLine Orbit;
            public double OrbitSize;
            public Vector3 Position;
            public float ShownRadius;
            public Vector3 Screen;
            public float PixelRadius;
            public bool Visible;
        }

        sealed class MapLine
        {
            public GameObject Go;
            public Mesh Mesh;
            public Material Material;
            public double Norm;
            public int Anchor;
        }

        sealed class SurfaceData
        {
            public int Body;
            public Vector3[] Vertices, Normals;
            public Color[] Colors;
            public int[] Triangles;
        }

        SpaceSimBootstrap boot;
        Camera flightCamera, mapCamera;
        GameObject root;
        Shader lineShader;
        readonly List<MapBody> bodies = new List<MapBody>();
        readonly List<MapLine> trajectory = new List<MapLine>();
        readonly ConcurrentQueue<SurfaceData> finishedSurfaces = new ConcurrentQueue<SurfaceData>();
        volatile bool disposed;

        bool open;
        int focus = int.MinValue;
        int viewedActiveBody = -1;
        float yaw = 35f, pitch = 35f, targetYaw = 35f, targetPitch = 35f;
        double distance = 3e7, targetDistance = 3e7;
        double systemExtent = 1e12;
        Vector3d focusBlend, viewFocus;
        double scale = 1.0;
        Vector3 cameraPosition;
        float unitsPerPixel = 0.001f;
        TrajectoryPrediction shownPrediction, shownNodePrediction;
        readonly List<MapLine> nodeTrajectory = new List<MapLine>();

        /// <summary>The trajectory after the planned burn (dashed).</summary>
        static readonly Color NodeColor = HudStyle.Violet;
        /// <summary>Handle colours: X prograde, Y normal, Z radial.</summary>
        static readonly Color[] AxisColors = { HudStyle.Green, HudStyle.Violet, HudStyle.Cyan };
        const float HandlePixels = 64f, HandlePick = 12f, TrajectoryPick = 10f;

        // Node marker and handles (screen points in GUI coordinates, computed in LateUpdate).
        bool nodeShown, nodeOccluded;
        Vector2 nodeGui;
        readonly Vector2[] handleGui = new Vector2[6];
        readonly bool[] handleShown = new bool[6];
        bool selectedShown;
        Vector2 selectedGui;
        // Dragging a handle: axis 0..2, sign ±1, where the drag started (GUI) and the handle's screen direction.
        int dragHandle = -1;
        Vector2 dragStart, dragDir;
        Vector2 panelScroll;
        float panelContentHeight = 600f;
        double editStep = 10.0;
        readonly string[] fieldText = { "", "", "" };
        int transferTarget = int.MinValue;

        VehicleOrbitInfo orbit;
        Vector3 vehicleScreen, periapsisScreen, apoapsisScreen;
        bool vehicleOccluded, periapsisOccluded, apoapsisOccluded, hasVehicle, showPeriapsis, showApoapsis;

        public bool IsOpen => open;
        /// <summary>Distance from the map camera's pivot [m] (the zoom level).</summary>
        public double ViewDistance => targetDistance;
        /// <summary>Index of the body in the centre of the map (−1 = star, −2 = the vehicle).</summary>
        public int Focus => focus;
        public string FocusName => FocusLabel(focus);

        public void Initialize(SpaceSimBootstrap bootstrap, Camera flightViewCamera, StarfieldController starfield)
        {
            boot = bootstrap;
            flightCamera = flightViewCamera;
            FlightInput.MapOpen = false;

            Shader bodyShader = Shader.Find("SpaceSim/MapBody");
            lineShader = Shader.Find("SpaceSim/MapLine");
            if (bodyShader == null || lineShader == null)
            {
                Debug.LogError("[SpaceSim] Map shaders not found (Resources/Shaders).", this);
                return;
            }

            root = new GameObject("MapScene");
            root.transform.SetParent(transform, false);
            root.SetActive(false);

            var cameraGo = new GameObject("MapCamera");
            cameraGo.transform.SetParent(root.transform, false);
            cameraGo.layer = MapLayer;
            mapCamera = cameraGo.AddComponent<Camera>();
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = Color.black;
            mapCamera.cullingMask = 1 << MapLayer;
            mapCamera.fieldOfView = FieldOfView;
            mapCamera.nearClipPlane = NearPlane;
            mapCamera.farClipPlane = FarPlane;
            mapCamera.enabled = false;

            if (starfield != null && starfield.StarMesh != null) AddStars(starfield);
            BuildBodies(bodyShader);
            StartSurfaceBuild();
        }

        void AddStars(StarfieldController starfield)
        {
            var go = new GameObject("MapStars");
            go.transform.SetParent(root.transform, false);
            go.layer = MapLayer;
            var material = new Material(starfield.StarShader) { name = "MapStars" };
            material.SetFloat("_Brightness", 2.5f);
            material.SetFloat("_PixelSize", 1.6f);
            go.AddComponent<MeshFilter>().sharedMesh = starfield.StarMesh;
            Configure(go.AddComponent<MeshRenderer>(), material);
            // The same galactic band as in flight (the map sky is always "in space": full visibility).
            Material galaxy = StarfieldController.CreateMilkyWayMaterial(starfield.MilkyWayIntensity);
            if (galaxy != null) starfield.AddMilkyWay(root.transform, MapLayer, galaxy);
        }

        static void Configure(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void BuildBodies(Shader bodyShader)
        {
            StarSystemRuntime system = boot.System;
            PatchedConicSystem sys = system.Navigation;
            Mesh sphere = ProceduralMeshes.UvSphere(64, 32, "MapSphere");
            var white = new Color[sphere.vertexCount];
            for (int i = 0; i < white.Length; i++) white[i] = new Color(0.5f, 0.5f, 0.5f, 0f);
            sphere.colors = white;

            var star = NewBody(PatchedConicSystem.Star, sys.NameOf(PatchedConicSystem.Star), system.Star.radius, 7f, null, sphere, bodyShader);
            star.Renderer.sharedMaterial.SetColor(ColorId, new Color(system.Star.color.r, system.Star.color.g, system.Star.color.b).linear * 1.6f);
            star.Renderer.sharedMaterial.SetFloat(EmissiveId, 1f);
            bodies.Add(star);

            double extent = star.Radius * 10.0;
            for (int i = 0; i < sys.Count; i++)
            {
                NavBody nav = sys[i];
                PlanetRuntime runtime = system.Bodies[i].Runtime;
                MapBody body = NewBody(i, nav.Name, nav.Radius, nav.ParentIndex < 0 ? 4.5f : 3f, runtime, sphere, bodyShader);
                body.Renderer.sharedMaterial.SetColor(RimId, AtmosphereRim(runtime));
                body.OrbitSize = nav.Orbit.SemiMajorAxis * (1.0 + nav.Orbit.Eccentricity);
                body.Orbit = BuildOrbitLine(nav);
                bodies.Add(body);
                if (nav.ParentIndex < 0) extent = Math.Max(extent, body.OrbitSize);
            }
            systemExtent = extent;
        }

        MapBody NewBody(int index, string bodyName, double radius, float minPixels, PlanetRuntime runtime, Mesh mesh, Shader shader)
        {
            var go = new GameObject("MapBody · " + bodyName);
            go.transform.SetParent(root.transform, false);
            go.layer = MapLayer;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            Configure(renderer, new Material(shader) { name = "MapBody" });
            return new MapBody
            {
                Index = index, Name = bodyName, Radius = radius, MinPixels = minPixels, Runtime = runtime,
                Transform = go.transform, Renderer = renderer, Filter = filter
            };
        }

        /// <summary>Colour of the air glow: the planet's Rayleigh scattering (blue for thin clean air, its own tint elsewhere).</summary>
        static Color AtmosphereRim(PlanetRuntime runtime)
        {
            if (runtime.Model == null || runtime.Model.Atmosphere.ReferenceDensity <= 0.0) return new Color(0f, 0f, 0f, 0f);
            Vector3d beta = runtime.Definition.optics.rayleighScattering;
            var tint = runtime.Definition.generation.atmosphereTint;
            double r = beta.x * tint.r, g = beta.y * tint.g, b = beta.z * tint.b;
            double max = Math.Max(r, Math.Max(g, b));
            if (!(max > 0.0)) return new Color(0.5f, 0.6f, 0.8f, 0.5f);
            return new Color((float)(r / max), (float)(g / max), (float)(b / max), 0.55f);
        }

        // ---- Surfaces ----------------------------------------------------------------------------------------------

        /// <summary>The surfaces come from the terrain generator on a worker thread; until then a body is a plain grey sphere.</summary>
        void StartSurfaceBuild()
        {
            var jobs = new List<(int body, PlanetSurfaceSampler sampler)>();
            foreach (MapBody b in bodies)
                if (b.Runtime != null && b.Runtime.Sampler != null) jobs.Add((b.Index, b.Runtime.Sampler));
            Task.Run(() =>
            {
                foreach (var job in jobs)
                {
                    if (disposed) return;
                    finishedSurfaces.Enqueue(BuildSurface(job.body, job.sampler));
                }
            }).ContinueWith(t => Debug.LogError("[SpaceSim] Map surface generation failed: " + t.Exception?.GetBaseException().Message),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        static SurfaceData BuildSurface(int body, PlanetSurfaceSampler sampler)
        {
            int perFace = 1 << SurfaceLevel;
            var chunks = new List<ChunkMeshData>(CubeSphere.FaceCount * perFace * perFace);
            int vertices = 0, indices = 0;
            for (int face = 0; face < CubeSphere.FaceCount; face++)
                for (int y = 0; y < perFace; y++)
                    for (int x = 0; x < perFace; x++)
                    {
                        ChunkMeshData chunk = ChunkMeshBuilder.Build(sampler, new ChunkId(face, SurfaceLevel, x, y), SurfaceResolution, 0.0);
                        chunks.Add(chunk);
                        vertices += chunk.VertexCount;
                        indices += chunk.Triangles.Length;
                    }

            var data = new SurfaceData
            {
                Body = body, Vertices = new Vector3[vertices], Normals = new Vector3[vertices], Colors = new Color[vertices], Triangles = new int[indices]
            };
            double radius = sampler.PlanetRadius;
            int v = 0, t = 0;
            foreach (ChunkMeshData c in chunks)
            {
                int first = v;
                for (int i = 0; i < c.VertexCount; i++, v++)
                {
                    // Same axis swap as CoordinateSystem.FillMesh; unit-radius mesh in planet-fixed axes.
                    data.Vertices[v] = new Vector3((float)((c.Center.x + c.Positions[i * 3]) / radius), (float)((c.Center.z + c.Positions[i * 3 + 2]) / radius),
                        (float)((c.Center.y + c.Positions[i * 3 + 1]) / radius));
                    data.Normals[v] = new Vector3(c.Normals[i * 3], c.Normals[i * 3 + 2], c.Normals[i * 3 + 1]);
                    data.Colors[v] = new Color(c.Colors[i * 4], c.Colors[i * 4 + 1], c.Colors[i * 4 + 2], c.Colors[i * 4 + 3]);
                }
                for (int i = 0; i < c.Triangles.Length; i += 3, t += 3)
                {
                    data.Triangles[t] = first + c.Triangles[i];
                    data.Triangles[t + 1] = first + c.Triangles[i + 2]; // the axis swap is a reflection: reverse the winding
                    data.Triangles[t + 2] = first + c.Triangles[i + 1];
                }
            }
            return data;
        }

        void ApplyFinishedSurface()
        {
            if (!finishedSurfaces.TryDequeue(out SurfaceData data)) return; // one per frame: a mesh upload is not free
            foreach (MapBody b in bodies)
            {
                if (b.Index != data.Body) continue;
                var mesh = new Mesh { name = "MapSurface · " + b.Name, indexFormat = IndexFormat.UInt32 };
                mesh.vertices = data.Vertices;
                mesh.normals = data.Normals;
                mesh.colors = data.Colors;
                mesh.triangles = data.Triangles;
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
                b.Filter.sharedMesh = mesh;
                return;
            }
        }

        // ---- Lines -------------------------------------------------------------------------------------------------

        MapLine NewLine(string lineName, Color color, float halfWidth)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(root.transform, false);
            go.layer = MapLayer;
            var line = new MapLine { Go = go, Mesh = new Mesh { name = lineName, indexFormat = IndexFormat.UInt32 }, Material = new Material(lineShader) { name = "MapLine" } };
            line.Material.SetColor(ColorId, color);
            line.Material.SetFloat(WidthId, halfWidth);
            go.AddComponent<MeshFilter>().sharedMesh = line.Mesh;
            Configure(go.AddComponent<MeshRenderer>(), line.Material);
            return line;
        }

        MapLine BuildOrbitLine(NavBody nav)
        {
            var points = new List<Vector3d>(OrbitSegments + 1);
            for (int i = 0; i <= OrbitSegments; i++) points.Add(nav.Orbit.Position(nav.Orbit.Period * i / OrbitSegments));
            MapLine line = NewLine("MapOrbit · " + nav.Name, OrbitColor, 0.75f);
            line.Anchor = nav.ParentIndex;
            FillLine(line, points);
            return line;
        }

        /// <summary>Vertices are normalised by the curve's size; the transform scales them back, so the mesh is independent of the zoom.</summary>
        static void FillLine(MapLine line, IList<Vector3d> points, bool dashed = false)
        {
            if (dashed)
            {
                // Short dashes: every chord split in four, every other piece drawn (a separate segment list).
                var pieces = new List<Vector3d>();
                for (int i = 0; i + 1 < points.Count; i++)
                    for (int k = 0; k < 4; k += 2)
                    {
                        pieces.Add(Vector3d.Lerp(points[i], points[i + 1], k / 4.0));
                        pieces.Add(Vector3d.Lerp(points[i], points[i + 1], (k + 1) / 4.0));
                    }
                FillSegments(line, pieces);
                return;
            }
            var pairs = new List<Vector3d>(Math.Max(0, 2 * (points.Count - 1)));
            for (int i = 0; i + 1 < points.Count; i++)
            {
                pairs.Add(points[i]);
                pairs.Add(points[i + 1]);
            }
            FillSegments(line, pairs);
        }

        /// <summary>Independent segments: points 2k and 2k+1 are the ends of segment k.</summary>
        static void FillSegments(MapLine line, IList<Vector3d> ends)
        {
            double norm = 1.0;
            foreach (Vector3d p in ends) norm = Math.Max(norm, p.Magnitude);
            line.Norm = norm;
            int segments = ends.Count / 2;
            var vertices = new Vector3[segments * 4];
            var other = new Vector2[segments * 4];
            var extra = new Vector2[segments * 4];
            var triangles = new int[segments * 6];
            for (int s = 0; s < segments; s++)
            {
                Vector3 a = CoordinateSystem.ToUnityVector(ends[2 * s] / norm), b = CoordinateSystem.ToUnityVector(ends[2 * s + 1] / norm);
                int v = s * 4;
                vertices[v] = a; other[v] = new Vector2(b.x, b.y); extra[v] = new Vector2(b.z, 1f);
                vertices[v + 1] = a; other[v + 1] = new Vector2(b.x, b.y); extra[v + 1] = new Vector2(b.z, -1f);
                // Seen from the far end the direction is reversed, so the same side has the opposite sign.
                vertices[v + 2] = b; other[v + 2] = new Vector2(a.x, a.y); extra[v + 2] = new Vector2(a.z, -1f);
                vertices[v + 3] = b; other[v + 3] = new Vector2(a.x, a.y); extra[v + 3] = new Vector2(a.z, 1f);
                int t = s * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 3;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 1;
            }
            line.Mesh.Clear();
            line.Mesh.vertices = vertices;
            line.Mesh.uv = other;
            line.Mesh.uv2 = extra;
            line.Mesh.triangles = triangles;
            line.Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
        }

        /// <summary>
        /// The trajectory the vehicle is on (solid) and, with a node, the trajectory after the burn (dashed, <see cref="NodeColor"/>),
        /// both straight from the patched-conic predictions of <see cref="NavigationService"/>.
        /// </summary>
        void RefreshTrajectory()
        {
            NavigationService nav = boot.Navigation;
            TrajectoryPrediction basePrediction = nav.BasePrediction ?? nav.Prediction;
            TrajectoryPrediction nodePrediction = nav.Plan.Departure != null && nav.Prediction != null && nav.Prediction.NodeApplied ? nav.Prediction : null;
            if (!ReferenceEquals(basePrediction, shownPrediction))
            {
                shownPrediction = basePrediction;
                FillPrediction(trajectory, basePrediction, "MapTrajectory", HudStyle.Accent, false);
            }
            if (!ReferenceEquals(nodePrediction, shownNodePrediction))
            {
                shownNodePrediction = nodePrediction;
                FillPrediction(nodeTrajectory, nodePrediction, "MapManeuverTrajectory", NodeColor, true);
            }
        }

        void FillPrediction(List<MapLine> lines, TrajectoryPrediction prediction, string lineName, Color color, bool dashed)
        {
            int used = 0;
            if (prediction != null)
            {
                foreach (PredictedPatch patch in prediction.Patches)
                {
                    if (patch.RelativePoints.Count < 2) continue;
                    if (used == lines.Count) lines.Add(NewLine(lineName, color, dashed ? 1.5f : 1.25f));
                    MapLine line = lines[used++];
                    line.Anchor = patch.Primary;
                    FillLine(line, patch.RelativePoints, dashed);
                    line.Go.SetActive(true);
                }
            }
            for (int i = used; i < lines.Count; i++) lines[i].Go.SetActive(false);
        }

        // ---- Opening and control -----------------------------------------------------------------------------------

        public void Toggle()
        {
            if (open) Close();
            else Open();
        }

        public void Open()
        {
            if (open || root == null || boot.Navigation.System == null) return;
            open = true;
            root.SetActive(true);
            flightCamera.enabled = false;
            mapCamera.enabled = true;
            FlightInput.MapOpen = true;
            shownPrediction = null;
            if (viewedActiveBody != boot.System.ActiveIndex)
            {
                viewedActiveBody = boot.System.ActiveIndex;
                focus = boot.Navigation.Origin;
                distance = targetDistance = Clamped(FrameDistance(focus));
                focusBlend = Vector3d.Zero;
            }
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            mapCamera.enabled = false;
            flightCamera.enabled = true;
            root.SetActive(false);
            FlightInput.MapOpen = false;
            dragHandle = -1;
        }

        /// <summary>Centres the map on a body (−1 = star, −2 = the vehicle) and frames it.</summary>
        public void SetFocus(int index)
        {
            if (index == focus) return;
            double now = boot.Simulation.RenderTime;
            focusBlend = viewFocus - FocusPosition(index, now);
            focus = index;
            targetDistance = Clamped(FrameDistance(index));
        }

        /// <summary>Zooms by wheel notches (+ = closer), exactly like the mouse wheel.</summary>
        public void Zoom(float notches) => targetDistance = Clamped(targetDistance * Math.Exp(-notches * ZoomPerNotch));

        double MinDistance(int index)
        {
            MapBody b = index == FocusVehicle ? null : BodyOf(index);
            return b != null ? b.Radius * 1.15 : VehicleMinDistance;
        }

        double Clamped(double value) => MathD.Clamp(value, MinDistance(focus), systemExtent * 6.0);

        MapBody BodyOf(int index)
        {
            foreach (MapBody b in bodies)
                if (b.Index == index) return b;
            return null;
        }

        double FrameDistance(int index)
        {
            VehicleOrbitInfo info = boot.Navigation.CurrentOrbit();
            if (index == FocusVehicle)
                return info != null ? Math.Max(1e5, 0.6 * info.Distance) : 0.5 * BodyOf(boot.Navigation.Origin).Radius;
            if (index == PatchedConicSystem.Star) return systemExtent * 2.2;
            double radius = BodyOf(index).Radius;
            if (info == null || info.Primary != index) return radius * 5.0;
            double reach = info.Apsides.IsBound && info.ApoapsisInsideSoi ? info.Apsides.ApoapsisRadius : info.Distance;
            return Math.Max(radius * 3.2, Math.Max(info.Distance, reach) * 2.6);
        }

        Vector3d FocusPosition(int index, double time) =>
            index == FocusVehicle ? boot.Navigation.VehicleStarPosition() : boot.Navigation.System.Position(index, time);

        string FocusLabel(int index) => index == FocusVehicle ? Loc.T("Аппарат") : Loc.T(boot.Navigation.System.NameOf(index));

        void OnDestroy()
        {
            disposed = true;
            if (open)
            {
                if (flightCamera != null) flightCamera.enabled = true;
                FlightInput.MapOpen = false;
            }
        }

        // ---- Per-frame ---------------------------------------------------------------------------------------------

        void LateUpdate()
        {
            ApplyFinishedSurface();
            if (!open || boot.Body == null || boot.Body.Simulation == null) return;
            PatchedConicSystem sys = boot.Navigation.System;
            if (sys == null) return;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            HandleInput(dt);
            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-RotationSmoothing * dt));
            pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-RotationSmoothing * dt));
            distance = Math.Exp(MathD.Lerp(Math.Log(distance), Math.Log(targetDistance), 1f - Mathf.Exp(-ZoomSmoothing * dt)));
            focusBlend *= Math.Exp(-FocusSmoothing * dt);

            double time = boot.Simulation.RenderTime;
            viewFocus = FocusPosition(focus, time) + focusBlend;
            scale = CameraDistance / distance;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            cameraPosition = rotation * new Vector3(0f, 0f, -CameraDistance);
            mapCamera.transform.SetPositionAndRotation(cameraPosition, rotation);
            unitsPerPixel = 2f * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;

            Shader.SetGlobalVector(StarId, CoordinateSystem.ToUnityVector((sys.Position(PatchedConicSystem.Star, time) - viewFocus) * scale));
            PlaceBodies(sys, time);
            RefreshTrajectory();
            PlaceLines(sys, time);
            PlaceMarkers(sys, time);
            PlaceNodeMarkers(sys, time);
            ApplyDrag(dt);
        }

        // ---- Manoeuvre node: marker, 3D handles, dragging ----------------------------------------------------------------

        /// <summary>
        /// The node sits on the trajectory where it will be (relative to its primary, drawn at the primary's current position like
        /// the trajectory itself). Its handles are the prograde (X), normal (Y) and radial (Z) directions of the pre-burn state,
        /// each ±, about <see cref="HandlePixels"/> long on screen whatever the zoom.
        /// </summary>
        void PlaceNodeMarkers(PatchedConicSystem sys, double time)
        {
            ManeuverController m = boot.Navigation.Maneuvers;
            nodeShown = selectedShown = false;
            for (int i = 0; i < 6; i++) handleShown[i] = false;
            if (m == null) return;
            if (m.HasSelection && Project(m.Selected.State.Position + sys.Position(m.Selected.State.Primary, time), out Vector3 sel, out _))
            {
                selectedShown = true;
                selectedGui = ToGui(sel);
            }
            if (m.Node == null || !m.Preview.Valid) return;
            CoastState pre = m.Preview.PreBurn;
            Vector3d at = pre.Position + sys.Position(pre.Primary, time);
            if (!Project(at, out Vector3 screen, out nodeOccluded)) return;
            nodeShown = true;
            nodeGui = ToGui(screen);
            if (m.NodeConfirmed) return;
            Vector3 mapPoint = ToMap(at, out float shrink);
            float toCamera = Vector3.Distance(mapPoint, cameraPosition);
            double length = HandlePixels * HudStyle.Scale * unitsPerPixel * toCamera / (scale * Math.Max(shrink, 1e-6f));
            ManeuverNode.Basis(pre.Position, pre.Velocity, out Vector3d p, out Vector3d n, out Vector3d r);
            Vector3d[] axes = { p, n, r };
            for (int i = 0; i < 6; i++)
            {
                Vector3d dir = axes[i / 2] * (i % 2 == 0 ? 1.0 : -1.0);
                if (Project(at + dir * length, out Vector3 tip, out _))
                {
                    handleGui[i] = ToGui(tip);
                    handleShown[i] = true;
                }
            }
        }

        /// <summary>
        /// While a handle is held the component changes at a rate that grows with how far the mouse is pulled along the handle
        /// (fine near the start, fast far away); pulling back the other way reduces it.
        /// </summary>
        void ApplyDrag(float dt)
        {
            ManeuverController m = boot.Navigation.Maneuvers;
            if (dragHandle < 0 || m == null || m.Node == null || m.NodeConfirmed)
            {
                dragHandle = -1;
                return;
            }
            Vector2 mouse = ToGui(FlightInput.MousePosition);
            float along = Dot2(mouse - dragStart, dragDir);
            float pixels = Mathf.Abs(along);
            if (pixels < 3f) return;
            double rate = Math.Pow(pixels / 40.0, 2.0) * 20.0; // m/s per real second
            int axis = dragHandle / 2;
            double sign = (dragHandle % 2 == 0 ? 1.0 : -1.0) * Math.Sign(along);
            m.AddComponent(axis, sign * rate * dt);
        }

        bool HandleNodeMouse(Event e)
        {
            ManeuverController m = boot.Navigation.Maneuvers;
            if (m == null) return false;
            if (e.type == EventType.MouseUp && dragHandle >= 0)
            {
                dragHandle = -1;
                e.Use();
                return true;
            }
            if (e.type != EventType.MouseDown || e.button != 0 || !nodeShown || m.NodeConfirmed) return false;
            for (int i = 0; i < 6; i++)
            {
                if (!handleShown[i] || Vector2.Distance(handleGui[i], e.mousePosition) > HandlePick) continue;
                dragHandle = i;
                dragStart = e.mousePosition;
                Vector2 d = handleGui[i] - nodeGui;
                float dl = Mathf.Sqrt(Dot2(d, d));
                dragDir = dl > 1e-2f ? new Vector2(d.x / dl, d.y / dl) : new Vector2(1f, 0f);
                GUIUtility.keyboardControl = 0;
                e.Use();
                return true;
            }
            return false;
        }

        void DrawNodeMarkers()
        {
            ManeuverController m = boot.Navigation.Maneuvers;
            if (m == null) return;
            if (selectedShown)
                Marker(new Vector3(selectedGui.x * HudStyle.Scale, Screen.height - selectedGui.y * HudStyle.Scale, 1f), false, HudStyle.Warning, 8f,
                    Loc.T("ТОЧКА"), Loc.T("через ") + SpaceSim.Units.UnitFormatter.Duration(m.Selected.Time - boot.Simulation.SimulationTime));
            if (!nodeShown) return;
            string[] names = { Loc.T("X ПРОГРЕЙД"), Loc.T("Y НОРМАЛЬ"), Loc.T("Z РАДИАЛЬНО") };
            for (int i = 0; i < 6; i++)
            {
                if (!handleShown[i]) continue;
                Color c = AxisColors[i / 2];
                if (i % 2 == 1) c = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, 1f);
                DottedLine(nodeGui, handleGui[i], c, dragHandle == i ? 3f : 2f);
                HudStyle.Dot(handleGui[i], dragHandle == i ? 14f : 11f, c);
                if (i % 2 == 0)
                    GUI.Label(new Rect(handleGui[i].x + 8f, handleGui[i].y - 8f, 140f, 16f), names[i / 2], HudStyle.Style(HudStyle.Face.Mono, 10, c));
                else
                    GUI.Label(new Rect(handleGui[i].x + 8f, handleGui[i].y - 8f, 30f, 16f), "−", HudStyle.Style(HudStyle.Face.Mono, 12, c));
            }
            ManeuverNode n = m.Node;
            Marker(new Vector3(nodeGui.x * HudStyle.Scale, Screen.height - nodeGui.y * HudStyle.Scale, 1f), nodeOccluded, NodeColor, 12f, Loc.T("МАНЁВР"),
                "Δv " + SpaceSim.Units.UnitFormatter.Number(n.Magnitude, 1) + Loc.T(" м/с"), 22f);
        }

        static float Dot2(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;

        static void DottedLine(Vector2 a, Vector2 b, Color c, float width)
        {
            float length = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(length / 2f));
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, (float)i / n);
                HudStyle.Fill(new Rect(p.x - width * 0.5f, p.y - width * 0.5f, width, width), c);
            }
        }

        /// <summary>Click on the trajectory (the one the vehicle is on): the nearest point of its polyline, snapped to the exact conic.</summary>
        bool PickTrajectory(Vector2 mouse, out TrajectoryPoint point)
        {
            point = default;
            NavigationService nav = boot.Navigation;
            TrajectoryPrediction p = shownPrediction;
            PatchedConicSystem sys = nav.System;
            FreeFlightSimulation sim = boot.Body.Simulation;
            if (p == null || sys == null || sim == null || sim.Navigation == null || sim.IsLanded) return false;
            double time = boot.Simulation.RenderTime;
            float best = TrajectoryPick;
            int bestPatch = -1, bestSample = 0;
            float bestFraction = 0f;
            for (int pi = 0; pi < p.Patches.Count; pi++)
            {
                PredictedPatch patch = p.Patches[pi];
                Vector3d anchor = sys.Position(patch.Primary, time);
                Vector2 prev = default;
                bool prevOk = false;
                for (int i = 0; i < patch.RelativePoints.Count; i++)
                {
                    Vector3 s = mapCamera.WorldToScreenPoint(ToMap(patch.RelativePoints[i] + anchor, out _));
                    bool ok = s.z > 0f;
                    Vector2 g = ToGui(s);
                    if (ok && prevOk)
                    {
                        Vector2 seg = g - prev;
                        float len2 = Dot2(seg, seg);
                        float f = len2 > 1e-6f ? Mathf.Clamp01(Dot2(mouse - prev, seg) / len2) : 0f;
                        float d = Vector2.Distance(mouse, prev + seg * f);
                        if (d < best)
                        {
                            best = d;
                            bestPatch = pi;
                            bestSample = i - 1;
                            bestFraction = f;
                        }
                    }
                    prev = g;
                    prevOk = ok;
                }
            }
            if (bestPatch < 0) return false;
            point = TrajectoryPoints.PointOnPatch(sys, p, bestPatch, bestSample, bestFraction, sim.CoastStateNow());
            return point.Valid;
        }

        void HandleInput(float dt)
        {
            if (FlightInput.IsTyping) return;
            if (FlightInput.RightMouseHeld || FlightInput.MiddleMouseHeld)
            {
                Vector2 d = FlightInput.LookDelta;
                targetYaw += d.x * RotateSpeed;
                targetPitch = Mathf.Clamp(targetPitch - d.y * RotateSpeed, -89f, 89f);
            }
            float notches = PointerOverUi() ? 0f : FlightInput.ScrollNotches;
            if (FlightInput.GetKey(InputKey.Equals)) notches += 20f * dt;
            if (FlightInput.GetKey(InputKey.Minus)) notches -= 20f * dt;
            if (notches != 0f) Zoom(notches);
        }

        bool PointerOverUi()
        {
            Vector2 mouse = FlightInput.MousePosition;
            float s = HudStyle.Scale;
            var point = new Vector2(mouse.x / s, (Screen.height - mouse.y) / s);
            return OverUi(point) || (boot.Hud != null && boot.Hud.WindowCovers(point));
        }

        /// <summary>Map-space position of a point; <paramref name="shrink"/> &lt; 1 when it had to be pulled inside the far plane.</summary>
        Vector3 ToMap(Vector3d position, out float shrink)
        {
            Vector3d relative = (position - viewFocus) * scale;
            double length = relative.Magnitude;
            shrink = length > FarLimit ? (float)(FarLimit / length) : 1f;
            return CoordinateSystem.ToUnityVector(relative * shrink);
        }

        void PlaceBodies(PatchedConicSystem sys, double time)
        {
            foreach (MapBody b in bodies)
            {
                b.Position = ToMap(sys.Position(b.Index, time), out float shrink);
                float toCamera = Vector3.Distance(b.Position, cameraPosition);
                // Bodies far smaller than a pixel are drawn at a minimum apparent size, otherwise they could never be seen or clicked.
                b.ShownRadius = Mathf.Max((float)(b.Radius * scale) * shrink, b.MinPixels * unitsPerPixel * toCamera);
                b.PixelRadius = b.ShownRadius / Mathf.Max(unitsPerPixel * toCamera, 1e-9f);
                b.Screen = mapCamera.WorldToScreenPoint(b.Position);
                b.Visible = true;
            }
            foreach (MapBody b in bodies)
            {
                int parent = b.Index >= 0 ? sys.Parent(b.Index) : PatchedConicSystem.Star;
                if (b.Index >= 0 && parent >= 0)
                {
                    // A moon that would sit on its parent's disk at this zoom only clutters it.
                    MapBody p = BodyOf(parent);
                    b.Visible = Vector2.Distance(b.Screen, p.Screen) > p.PixelRadius + b.PixelRadius + 3f;
                }
                b.Renderer.enabled = b.Visible;
                b.Transform.position = b.Position;
                b.Transform.localScale = Vector3.one * b.ShownRadius;
                if (b.Runtime != null) b.Transform.rotation = b.Runtime.UnityRotation(time);
            }
        }

        void PlaceLines(PatchedConicSystem sys, double time)
        {
            foreach (MapBody b in bodies)
            {
                if (b.Orbit == null) continue;
                float size = (float)(b.Orbit.Norm * scale) / (unitsPerPixel * CameraDistance); // radius on screen [px]
                float fade = Mathf.Clamp01((size - 8f) / 12f) * CurveFade(b.Orbit.Norm);
                bool target = boot.Navigation.Plan.Target == b.Index;
                Color c = target ? HudStyle.Warning : OrbitColor;
                c.a = (target ? 0.85f : 0.55f) * fade;
                b.Orbit.Material.SetColor(ColorId, c);
                b.Orbit.Go.SetActive(fade > 0.001f);
                Anchor(b.Orbit, sys, time);
            }
            foreach (MapLine line in trajectory)
            {
                if (!line.Go.activeSelf) continue;
                Color c = HudStyle.Accent;
                c.a = (shownNodePrediction != null ? 0.7f : 0.95f) * CurveFade(line.Norm);
                line.Material.SetColor(ColorId, c);
                Anchor(line, sys, time);
            }
            foreach (MapLine line in nodeTrajectory)
            {
                if (!line.Go.activeSelf) continue;
                Color c = NodeColor;
                c.a = 0.95f * CurveFade(line.Norm);
                line.Material.SetColor(ColorId, c);
                Anchor(line, sys, time);
            }
        }

        float CurveFade(double norm) => Mathf.Clamp01((float)(2.0 - norm / distance / (0.5 * MaxCurveToView)));

        void Anchor(MapLine line, PatchedConicSystem sys, double time)
        {
            line.Go.transform.position = CoordinateSystem.ToUnityVector((sys.Position(line.Anchor, time) - viewFocus) * scale);
            line.Go.transform.localScale = Vector3.one * (float)(line.Norm * scale);
        }

        void PlaceMarkers(PatchedConicSystem sys, double time)
        {
            orbit = boot.Navigation.CurrentOrbit();
            Vector3d vehicle = boot.Navigation.VehicleStarPosition();
            hasVehicle = Project(vehicle, out vehicleScreen, out vehicleOccluded);
            showPeriapsis = showApoapsis = false;
            if (orbit == null || !orbit.Apsides.HasLine) return;
            Vector3d center = sys.Position(orbit.Primary, time);
            showPeriapsis = Project(center + orbit.Apsides.PeriapsisPosition, out periapsisScreen, out periapsisOccluded);
            if (orbit.Apsides.IsBound && orbit.ApoapsisInsideSoi)
                showApoapsis = Project(center + orbit.Apsides.ApoapsisPosition, out apoapsisScreen, out apoapsisOccluded);
            // An orbit too small to read at this zoom: its marks would only pile up on the body.
            Vector3 body = BodyOf(orbit.Primary).Screen;
            if (Vector2.Distance(periapsisScreen, body) < ApsisMinPixels) showPeriapsis = false;
            if (Vector2.Distance(apoapsisScreen, body) < ApsisMinPixels) showApoapsis = false;
        }

        bool Project(Vector3d position, out Vector3 screen, out bool occluded)
        {
            Vector3 p = ToMap(position, out _);
            screen = mapCamera.WorldToScreenPoint(p);
            occluded = false;
            if (screen.z <= 0f) return false;
            Vector3 ray = p - cameraPosition;
            float length = ray.magnitude;
            ray /= length;
            foreach (MapBody b in bodies)
            {
                if (!b.Visible) continue;
                Vector3 toBody = b.Position - cameraPosition;
                float along = Vector3.Dot(toBody, ray);
                // Slightly smaller than the disk, so a point on the surface itself does not count as hidden behind it.
                if (along > 0f && along < length && (toBody - ray * along).sqrMagnitude < b.ShownRadius * b.ShownRadius * 0.98f &&
                    along < length - b.ShownRadius * 0.02f) occluded = true;
            }
            return true;
        }

        // ---- Interface ---------------------------------------------------------------------------------------------

        static Vector2 ToGui(Vector3 screen)
        {
            float s = HudStyle.Scale;
            return new Vector2(screen.x / s, (Screen.height - screen.y) / s);
        }

        void OnGUI()
        {
            if (!open || FlightInput.PauseMenuOpen || boot.Navigation.System == null) return;
            GUI.depth = 5; // behind the windows that can be opened over the map (lower depth is drawn on top)
            HudStyle.Begin();
            Vector2 screen = HudStyle.VirtualScreen;
            if (Event.current.type == EventType.Layout) uiRects.Clear();

            DrawBodyLabels();
            bool besideApsis = (showPeriapsis && Vector2.Distance(vehicleScreen, periapsisScreen) < LabelClearance) ||
                               (showApoapsis && Vector2.Distance(vehicleScreen, apoapsisScreen) < LabelClearance);
            if (hasVehicle) Marker(vehicleScreen, vehicleOccluded, Color.white, 9f, Loc.T("АППАРАТ"), "", besideApsis ? -18f : 0f);
            if (showPeriapsis)
                Marker(periapsisScreen, periapsisOccluded, orbit.PeriapsisBelowSurface ? HudStyle.Danger : HudStyle.Accent, 8f, Loc.T("ПЕРИЦЕНТР"), orbit.PeriapsisText);
            if (showApoapsis) Marker(apoapsisScreen, apoapsisOccluded, HudStyle.TextStrong, 8f, Loc.T("АПОЦЕНТР"), orbit.ApoapsisText);
            if (Event.current.type == EventType.MouseUp || !OverUi(Event.current.mousePosition)) HandleNodeMouse(Event.current);
            DrawNodeMarkers();

            DrawHeader(screen);
            DrawPanel(screen);
            FlightHUD.DrawWarpScale(Ui(new Rect(36f, screen.y - 76f, 520f, 46f)), boot.Simulation, Loc.T("карта"));
            DrawFooter(screen);
            HandleClick();
        }

        readonly System.Collections.Generic.List<Rect> uiRects = new System.Collections.Generic.List<Rect>();

        Rect Ui(Rect r)
        {
            uiRects.Add(r);
            return r;
        }

        bool OverUi(Vector2 p)
        {
            foreach (Rect r in uiRects) if (r.Contains(p)) return true;
            return false;
        }

        void DrawBodyLabels()
        {
            foreach (MapBody b in bodies)
            {
                if (!b.Visible || b.Screen.z <= 0f) continue;
                Vector2 p = ToGui(b.Screen);
                float offset = b.PixelRadius / HudStyle.Scale + 4f;
                if (offset > 200f) continue; // the body fills the view: its name is in the panel
                Color c = b.Index == focus ? HudStyle.Accent : HudStyle.TextStrong;
                GUI.Label(new Rect(p.x - 120f, p.y + offset, 240f, 20f), Loc.T(b.Name), HudStyle.Style(HudStyle.Face.Strong, 14, c, TextAnchor.MiddleCenter));
            }
        }

        /// <summary>A point on the map with a two-line label: caption (small caps) and value (mono).</summary>
        static void Marker(Vector3 screen, bool occluded, Color color, float size, string caption, string value, float labelShift = 0f)
        {
            Vector2 p = ToGui(screen);
            color.a = occluded ? 0.4f : 1f;
            HudStyle.Dot(p, size, color);
            HudStyle.Fill(new Rect(p.x + size, p.y + labelShift, 26f, 1f), new Color(color.r, color.g, color.b, 0.6f * color.a));
            float x = p.x + size + 30f;
            GUI.Label(new Rect(x, p.y - 16f + labelShift, 300f, 14f), caption, HudStyle.Style(HudStyle.Face.Mono, 10, new Color(HudStyle.TextMuted.r, HudStyle.TextMuted.g, HudStyle.TextMuted.b, color.a)));
            if (value.Length > 0)
                GUI.Label(new Rect(x, p.y - 2f + labelShift, 340f, 16f), value, HudStyle.Style(HudStyle.Face.Mono, 13, new Color(HudStyle.TextStrong.r, HudStyle.TextStrong.g, HudStyle.TextStrong.b, color.a)));
        }

        /// <summary>Top bar: the vehicle's name (big), the map's system; on the right «К полёту» and the simulation clock.</summary>
        void DrawHeader(Vector2 screen)
        {
            string title = boot.Hud != null ? boot.Hud.FlightTitle : Loc.T("Аппарат");
            GUI.Label(new Rect(36f, 22f, 900f, 52f), title.ToUpperInvariant(), HudStyle.Style(HudStyle.Face.DisplayStrong, 40, HudStyle.TextStrong));
            GUI.Label(new Rect(38f, 72f, 600f, 16f), Loc.T("КАРТА") + "  ·  " + boot.System.SystemName.ToUpperInvariant() + "  ·  " + Loc.T("ЦЕНТР: ") +
                FocusLabel(focus).ToUpperInvariant() + "  ·  " + NavigationUI.Dist(distance), HudStyle.Style(HudStyle.Face.Mono, 11, HudStyle.TextMuted));
            HudStyle.HLine(36f, 96f, screen.x - 72f, new Color(1f, 1f, 1f, 0.10f));

            GUIStyle clock = HudStyle.Style(HudStyle.Face.Mono, 13, HudStyle.TextSoft, TextAnchor.MiddleRight);
            GUI.Label(new Rect(screen.x - 36f - 260f, 34f, 260f, 20f), SpaceSim.Units.UnitFormatter.Duration(boot.Simulation.SimulationTime), clock);
            var back = Ui(new Rect(screen.x - 36f - 260f - 190f, 30f, 170f, 28f));
            if (GUI.Button(back, Loc.T("К ПОЛЁТУ") + "  " + FlightInput.KeyLabel(InputKey.M), HudStyle.BigButton)) Close();
        }

        /// <summary>
        /// The right side panel (compact, scrolls): warp to a point, the selected point, the manoeuvre node editor with its preview and
        /// execution, the current orbit, the transfer planner and the bodies to centre the map on.
        /// </summary>
        void DrawPanel(Vector2 screen)
        {
            const float w = 350f;
            float x = screen.x - 24f - w, y = 108f;
            float h = Mathf.Min(screen.y - 108f - 96f, 820f);
            var r = Ui(new Rect(x, y, w, h));
            HudStyle.Card(r, 0.9f);
            var view = new Rect(0f, 0f, w - 18f, Mathf.Max(h - 4f, panelContentHeight));
            panelScroll = GUI.BeginScrollView(new Rect(x, y + 2f, w, h - 4f), panelScroll, view);
            float ix = 14f, iw = w - 18f - 26f, cy = 12f;
            GUIStyle k = HudStyle.Style(HudStyle.Face.Body, 12, HudStyle.TextPanel), v = HudStyle.Style(HudStyle.Face.Mono, 12, HudStyle.TextStrong, TextAnchor.MiddleRight);
            ManeuverController m = boot.Navigation.Maneuvers;

            if (m != null && m.Warping)
            {
                ManeuverOverlay.WarpReadout(m, ix, ref cy, iw, k, v);
                if (GUI.Button(new Rect(ix, cy + 2f, iw, 26f), Loc.T("ОТМЕНИТЬ УСКОРЕНИЕ") + "  " + FlightInput.KeyLabel(InputKey.WarpToTarget), HudStyle.Button)) m.CancelWarp();
                cy += 36f;
                Separator(ix, ref cy, iw);
            }
            if (m != null && m.HasSelection) DrawSelection(m, ix, ref cy, iw, k, v);
            if (m != null && m.Node != null) DrawNodeEditor(m, ix, ref cy, iw, k, v);
            else DrawOrbit(ix, ref cy, iw, k, v);
            if (m != null) DrawTransfer(m, ix, ref cy, iw, k, v);

            GUI.Label(new Rect(ix, cy, iw, 14f), Loc.T("ПОКАЗАТЬ"), HudStyle.Style(HudStyle.Face.Mono, 11, HudStyle.TextSoft));
            cy += 18f;
            float bx = ix, rowH = 24f;
            FocusChip(ref bx, ref cy, ix, iw, rowH, FocusVehicle, Loc.T("Аппарат"));
            foreach (MapBody b in bodies) FocusChip(ref bx, ref cy, ix, iw, rowH, b.Index, Loc.T(b.Name));
            cy += rowH + 10f;
            GUI.EndScrollView();
            if (Event.current.type == EventType.Repaint) panelContentHeight = cy;
        }

        static void Separator(float x, ref float y, float w)
        {
            HudStyle.HLine(x, y, w, new Color(1f, 1f, 1f, 0.14f));
            y += 8f;
        }

        static void Section(float x, ref float y, float w, string title, Color color)
        {
            GUI.Label(new Rect(x, y, w, 14f), title, HudStyle.Style(HudStyle.Face.Mono, 11, color));
            y += 18f;
        }

        static void Line(float x, ref float y, float w, string key, string value, GUIStyle k, GUIStyle v)
        {
            GUI.Label(new Rect(x, y, w * 0.6f, 18f), key, k);
            GUI.Label(new Rect(x + w * 0.3f, y, w * 0.7f, 18f), value, v);
            y += 19f;
        }

        static string Alt(double radius, double bodyRadius) =>
            double.IsInfinity(radius) ? Loc.T("нет (незамкнута)") : NavigationUI.Dist(radius - bodyRadius);

        static string Speed(double ms) => SpaceSim.Units.UnitFormatter.Number(ms, 1) + Loc.T(" м/с");

        void DrawSelection(ManeuverController m, float x, ref float y, float w, GUIStyle k, GUIStyle v)
        {
            TrajectoryPoint p = m.Selected;
            PatchedConicSystem sys = boot.Navigation.System;
            Section(x, ref y, w, Loc.T("ТОЧКА НА ТРАЕКТОРИИ") + " · " + Loc.T(sys.NameOf(p.State.Primary)).ToUpperInvariant(), HudStyle.Warning);
            double now = boot.Simulation.SimulationTime;
            double bodyRadius = p.State.Primary < 0 ? sys.StarProperties.radius : sys[p.State.Primary].Radius;
            Line(x, ref y, w, Loc.T("Через"), SpaceSim.Units.UnitFormatter.Duration(p.Time - now), k, v);
            Line(x, ref y, w, Loc.T("Прибытие (часы симуляции)"), SpaceSim.Units.UnitFormatter.Duration(p.Time), k, v);
            Line(x, ref y, w, Loc.T("Высота"), NavigationUI.Dist(p.State.Position.Magnitude - bodyRadius), k, v);
            Line(x, ref y, w, Loc.T("Скорость в точке"), Speed(p.State.Velocity.Magnitude), k, v);
            Line(x, ref y, w, Loc.T("Скорость сейчас"), Speed(m.CurrentSpeed()), k, v);
            float bw = (w - 8f) / 3f;
            if (GUI.Button(new Rect(x, y + 2f, bw, 26f), Loc.T("УСКОРИТЬ СЮДА"), HudStyle.Primary)) m.WarpToSelected();
            if (GUI.Button(new Rect(x + bw + 4f, y + 2f, bw, 26f), Loc.T("УЗЕЛ ЗДЕСЬ"), HudStyle.Button)) m.CreateNodeAtSelected();
            if (GUI.Button(new Rect(x + 2f * (bw + 4f), y + 2f, bw, 26f), Loc.T("СНЯТЬ"), HudStyle.Button)) m.ClearSelection();
            y += 36f;
            Separator(x, ref y, w);
        }

        void DrawNodeEditor(ManeuverController m, float x, ref float y, float w, GUIStyle k, GUIStyle v)
        {
            NavigationService nav = boot.Navigation;
            PatchedConicSystem sys = nav.System;
            ManeuverNode n = m.Node;
            NodePreview pv = m.Preview;
            NodeGuidance g = nav.Guidance;
            double now = boot.Simulation.SimulationTime;
            string state = m.Mode == BurnMode.Assisted ? Loc.T("ВЫПОЛНЯЕТСЯ") : m.Mode == BurnMode.InstantSandbox ? Loc.T("ПЕСОЧНИЦА") :
                m.NodeConfirmed ? Loc.T("ПОДТВЕРЖДЁН") : Loc.T("РЕДАКТИРУЕТСЯ");
            Section(x, ref y, w, Loc.T("МАНЁВР") + " · " + state, NodeColor);
            GUI.Label(new Rect(x, y, w * 0.5f, 40f), SpaceSim.Units.UnitFormatter.Number(n.Magnitude, 1), HudStyle.Style(HudStyle.Face.DisplayStrong, 34, HudStyle.TextStrong));
            string ign = g.Active ? (g.Started ? Loc.T("ИДЁТ") : g.TimeToIgnition > 0 ? Short(g.TimeToIgnition) : Loc.T("СЕЙЧАС")) : Short(n.Time - now);
            GUI.Label(new Rect(x + w * 0.5f, y, w * 0.5f, 40f), ign, HudStyle.Style(HudStyle.Face.DisplayStrong, 26, HudStyle.Blue, TextAnchor.MiddleRight));
            y += 40f;
            GUI.Label(new Rect(x, y, w, 14f), Loc.T("Δv ВСЕГО, М/С") + "  ·  " + Loc.T("ДО ЗАЖИГАНИЯ"), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
            y += 18f;

            // Components: X prograde, Y normal, Z radial — buttons by the step, typed values.
            bool editable = !m.NodeConfirmed && m.Mode == BurnMode.None;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && editable;
            string[] names = { Loc.T("X прогрейд"), Loc.T("Y нормаль"), Loc.T("Z радиально") };
            double[] values = { n.Prograde, n.Normal, n.Radial };
            for (int a = 0; a < 3; a++)
            {
                GUI.Label(new Rect(x, y, 96f, 24f), names[a], HudStyle.Style(HudStyle.Face.Mono, 11, AxisColors[a]));
                if (GUI.Button(new Rect(x + 98f, y, 28f, 22f), "−", HudStyle.Button)) m.AddComponent(a, -editStep);
                string name = "node-field-" + a;
                if (GUI.GetNameOfFocusedControl() != name) fieldText[a] = SpaceSim.Units.UnitFormatter.Number(values[a], 1);
                GUI.SetNextControlName(name);
                string text = GUI.TextField(new Rect(x + 130f, y, w - 130f - 32f, 22f), fieldText[a], HudStyle.Field);
                if (text != fieldText[a])
                {
                    fieldText[a] = text;
                    if (SpaceSim.Academy.AcademyNumber.TryParse(text, out double typed))
                        m.SetComponents(a == 0 ? typed : n.Prograde, a == 1 ? typed : n.Normal, a == 2 ? typed : n.Radial);
                }
                if (GUI.Button(new Rect(x + w - 28f, y, 28f, 22f), "+", HudStyle.Button)) m.AddComponent(a, editStep);
                y += 26f;
            }
            GUI.Label(new Rect(x, y, 60f, 22f), Loc.T("Шаг"), k);
            double[] steps = { 0.1, 1.0, 10.0, 100.0 };
            float sw = (w - 60f) / steps.Length;
            for (int i = 0; i < steps.Length; i++)
                if (GUI.Toggle(new Rect(x + 60f + i * sw, y, sw - 3f, 22f), editStep == steps[i], SpaceSim.Units.UnitFormatter.Number(steps[i], steps[i] < 1 ? 1 : 0), HudStyle.Button))
                    editStep = steps[i];
            y += 26f;
            GUI.Label(new Rect(x, y, 60f, 22f), Loc.T("Время"), k);
            double[] shifts = { -600.0, -60.0, 60.0, 600.0 };
            string[] shiftNames = { Loc.T("−10 мин"), Loc.T("−1 мин"), Loc.T("+1 мин"), Loc.T("+10 мин") };
            for (int i = 0; i < shifts.Length; i++)
                if (GUI.Button(new Rect(x + 60f + i * sw, y, sw - 3f, 22f), shiftNames[i], HudStyle.Button)) m.ShiftNode(shifts[i]);
            y += 28f;
            GUI.enabled = wasEnabled;
            if (editable)
            {
                GUI.Label(new Rect(x, y, w, 30f), Loc.T("Тяните ручки X/Y/Z на карте: чем дальше, тем быстрее меняется Δv."), HudStyle.Small);
                y += 30f;
            }

            // Cost for the real rocket.
            BurnEstimate b = pv.Burn;
            Line(x, ref y, w, Loc.T("Работа двигателя"), double.IsInfinity(b.Duration) ? "∞" : SpaceSim.Units.UnitFormatter.Duration(b.Duration), k, v);
            Line(x, ref y, w, Loc.T("Топливо (Циолковский)"), SpaceSim.Units.UnitFormatter.Number(b.Propellant, 0) + Loc.T(" кг") +
                (b.StagesUsed > 1 ? " · " + b.StagesUsed + Loc.T(" ступ.") : ""), k, v);
            GUIStyle ok = HudStyle.Style(HudStyle.Face.Mono, 12, b.Sufficient ? HudStyle.Success : HudStyle.Danger, TextAnchor.MiddleRight);
            Line(x, ref y, w, Loc.T("Запас Δv"), SpaceSim.Units.UnitFormatter.Number(b.Available, 0) + Loc.T(" м/с") + (b.Sufficient ? Loc.T(" · хватает") : Loc.T(" · НЕ ХВАТАЕТ")), k, ok);

            // Orbit after the burn.
            if (pv.Valid)
            {
                y += 4f;
                GUI.Label(new Rect(x, y, w, 14f), Loc.T("ОРБИТА ПОСЛЕ МАНЁВРА · ") + Loc.T(sys.NameOf(pv.After.Primary)).ToUpperInvariant(), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
                y += 16f;
                Line(x, ref y, w, Loc.T("Апоцентр"), Alt(pv.Before.ApoapsisRadius, pv.BodyRadius) + " → " + Alt(pv.After.ApoapsisRadius, pv.BodyRadius), k, v);
                Line(x, ref y, w, Loc.T("Перицентр"), Alt(pv.Before.PeriapsisRadius, pv.BodyRadius) + " → " + Alt(pv.After.PeriapsisRadius, pv.BodyRadius),
                    k, pv.After.PeriapsisRadius < pv.BodyRadius ? HudStyle.Style(HudStyle.Face.Mono, 12, HudStyle.Danger, TextAnchor.MiddleRight) : v);
                Line(x, ref y, w, Loc.T("Наклонение"), SpaceSim.Units.UnitFormatter.Number(pv.Before.Inclination * MathD.RadToDeg, 2) + "° → " +
                    SpaceSim.Units.UnitFormatter.Number(pv.After.Inclination * MathD.RadToDeg, 2) + "°", k, v);
                Line(x, ref y, w, Loc.T("Период"), (pv.After.Bound ? SpaceSim.Units.UnitFormatter.Duration(pv.After.Period) : "—"), k, v);
            }
            foreach (EncounterInfo e in pv.Encounters)
            {
                string body = Loc.T(sys.NameOf(e.Body));
                Line(x, ref y, w, Loc.T("Сфера ") + body, Loc.T("через ") + SpaceSim.Units.UnitFormatter.Duration(e.Time - now), k, v);
                Line(x, ref y, w, Loc.T("  сближение"), e.Impact ? Loc.T("СТОЛКНОВЕНИЕ") : NavigationUI.Dist(e.Periapsis - sys[e.Body].Radius) + Loc.T(" над поверхностью"), k,
                    e.Impact ? HudStyle.Style(HudStyle.Face.Mono, 12, HudStyle.Danger, TextAnchor.MiddleRight) : v);
            }
            int target = nav.Plan.Target;
            if (target >= 0 && !double.IsNaN(pv.TargetClosest))
            {
                bool hit = nav.Prediction != null && nav.Prediction.Encounter;
                Line(x, ref y, w, Loc.T(sys.NameOf(target)), (hit ? Loc.T("перицентр ") : Loc.T("промах ")) + NavigationUI.Dist(pv.TargetClosest) +
                    Loc.T(", через ") + SpaceSim.Units.UnitFormatter.Duration(pv.TargetClosestTime - now), k, v);
            }

            // Execution.
            y += 6f;
            float bw = (w - 4f) / 2f;
            if (m.Mode != BurnMode.None)
            {
                GUI.Label(new Rect(x, y, w, 18f), Loc.T("Состояние: ") + m.BurnStatus + " · Δv " + Loc.T("осталось ") + Speed(g.Remaining), HudStyle.Small);
                y += 20f;
                if (GUI.Button(new Rect(x, y, w, 26f), Loc.T("ПРЕРВАТЬ МАНЁВР"), HudStyle.Button)) m.AbortBurn();
                y += 30f;
            }
            else if (!m.NodeConfirmed)
            {
                if (GUI.Button(new Rect(x, y, bw, 26f), Loc.T("ПОДТВЕРДИТЬ"), HudStyle.Primary)) m.ConfirmNode(true);
                if (GUI.Button(new Rect(x + bw + 4f, y, bw, 26f), Loc.T("УДАЛИТЬ"), HudStyle.Button)) m.DeleteNode();
                y += 30f;
            }
            else
            {
                if (GUI.Button(new Rect(x, y, bw, 26f), Loc.T("ИЗМЕНИТЬ"), HudStyle.Button)) m.ConfirmNode(false);
                if (GUI.Button(new Rect(x + bw + 4f, y, bw, 26f), Loc.T("УДАЛИТЬ"), HudStyle.Button)) m.DeleteNode();
                y += 30f;
                bool warping = m.Warping && m.WarpIsToNode;
                if (GUI.Button(new Rect(x, y, w, 26f), warping ? Loc.T("ОСТАНОВИТЬ УСКОРЕНИЕ") : Loc.T("УСКОРИТЬ ДО МАНЁВРА") + "  " + FlightInput.KeyLabel(InputKey.WarpToTarget), HudStyle.Button))
                {
                    if (warping) m.CancelWarp();
                    else m.WarpToNode();
                }
                y += 30f;
                if (GUI.Button(new Rect(x, y, w, 26f), Loc.T("ВЫПОЛНИТЬ С ПОМОЩНИКОМ (ДВИГАТЕЛЬ)"), HudStyle.Primary)) m.StartAssistedBurn();
                y += 30f;
                if (m.InstantBurnAllowed) // Sandbox only: a Career pays for its Δv
                {
                    if (GUI.Button(new Rect(x, y, w, 24f), Loc.T("МГНОВЕННО — ПЕСОЧНИЦА / ЧИТ (без топлива)"), HudStyle.Button)) m.StartInstantSandbox();
                    y += 28f;
                }
                GUI.Label(new Rect(x, y, w, 44f), Loc.T("Вручную: развернитесь на тангаж ") + SpaceSim.Units.UnitFormatter.Number(g.Pitch, 1) + Loc.T("°, курс ") +
                    SpaceSim.Units.UnitFormatter.Number(g.Heading, 1) + Loc.T("° и включите двигатель за половину прожига до узла; остаток Δv — в HUD."), HudStyle.Small);
                y += 46f;
            }
            Separator(x, ref y, w);
        }

        void DrawOrbit(float x, ref float y, float w, GUIStyle k, GUIStyle v)
        {
            Section(x, ref y, w, orbit != null ? Loc.T("ОРБИТА · ВОКРУГ ") + orbit.PrimaryName.ToUpperInvariant() : Loc.T("ОРБИТА"), HudStyle.Blue);
            if (orbit == null)
            {
                GUI.Label(new Rect(x, y, w, 36f), Loc.T("Аппарат на поверхности — орбиты нет."), HudStyle.Body);
                y += 40f;
            }
            else
            {
                Line(x, ref y, w, Loc.T("Перицентр"), orbit.PeriapsisText, k, v);
                Line(x, ref y, w, Loc.T("Апоцентр"), orbit.ApoapsisText, k, v);
                Line(x, ref y, w, Loc.T("До перицентра"), orbit.TimeToPeriapsisText, k, v);
                Line(x, ref y, w, Loc.T("До апоцентра"), orbit.TimeToApoapsisText, k, v);
                Line(x, ref y, w, Loc.T("Период"), orbit.PeriodText, k, v);
                Line(x, ref y, w, Loc.T("Эксцентриситет"), SpaceSim.Units.UnitFormatter.Number(orbit.Apsides.Eccentricity, 4), k, v);
                GUI.Label(new Rect(x, y, w, 30f), Loc.T("Щёлкните по траектории: точка для ускорения времени или узла манёвра."), HudStyle.Small);
                y += 32f;
            }
            Separator(x, ref y, w);
        }

        /// <summary>Transfer planner: pick a destination, see Hohmann/Lambert numbers and the window, put the node at the window.</summary>
        void DrawTransfer(ManeuverController m, float x, ref float y, float w, GUIStyle k, GUIStyle v)
        {
            NavigationService nav = boot.Navigation;
            PatchedConicSystem sys = nav.System;
            FreeFlightSimulation sim = boot.Body != null ? boot.Body.Simulation : null;
            if (sys == null || sim == null || sim.Navigation == null || sim.IsLanded) return;
            int primary = sim.Primary;
            Section(x, ref y, w, Loc.T("ПЕРЕЛЁТ К ТЕЛУ"), HudStyle.Warning);
            if (transferTarget == int.MinValue) transferTarget = nav.Plan.Target;
            float bx = x, rowH = 24f;
            for (int i = 0; i < sys.Count; i++)
            {
                NavBody b = sys[i];
                bool reachable = i != primary && (b.ParentIndex == primary || (primary >= 0 && b.ParentIndex == sys.Parent(primary)));
                if (!reachable) continue;
                string label = Loc.T(b.Name);
                float cw = HudStyle.Button.CalcSize(new GUIContent(label)).x + 16f;
                if (bx + cw > x + w)
                {
                    bx = x;
                    y += rowH + 4f;
                }
                bool on = transferTarget == i;
                if (GUI.Toggle(new Rect(bx, y, cw, rowH), on, label, HudStyle.Button) && !on)
                {
                    transferTarget = i;
                    nav.SetTarget(i);
                    m.PlanTransfer(i, false);
                }
                bx += cw + 4f;
            }
            y += rowH + 8f;
            if (transferTarget < 0 || transferTarget >= sys.Count)
            {
                GUI.Label(new Rect(x, y, w, 30f), Loc.T("Выберите цель: Гоман, окно запуска и решение Ламберта с реальными положениями тел."), HudStyle.Small);
                y += 34f;
                Separator(x, ref y, w);
                return;
            }
            TransferPlan p = m.Transfer;
            double now = boot.Simulation.SimulationTime;
            if (p != null && p.Target == transferTarget && p.Kind != TransferKind.None && p.Error == null)
            {
                Line(x, ref y, w, Loc.T("Гоман: Δv отлёта / захвата"), SpaceSim.Units.UnitFormatter.Number(p.HohmannDeltaV1, 0) + " / " + SpaceSim.Units.UnitFormatter.Number(p.HohmannDeltaV2, 0) + Loc.T(" м/с"), k, v);
                Line(x, ref y, w, Loc.T("Гоман: время полёта"), SpaceSim.Units.UnitFormatter.Duration(p.HohmannTime), k, v);
                Line(x, ref y, w, Loc.T("Фазовый угол: нужен / сейчас"), SpaceSim.Units.UnitFormatter.Number(p.RequiredPhase * MathD.RadToDeg, 1) + "° / " +
                    SpaceSim.Units.UnitFormatter.Number(p.CurrentPhase * MathD.RadToDeg, 1) + "°", k, v);
                Line(x, ref y, w, Loc.T("Окно (Гоман) через"), SpaceSim.Units.UnitFormatter.Duration(p.WindowTime - now), k, v);
                if (!double.IsInfinity(p.SynodicPeriod)) Line(x, ref y, w, Loc.T("Синодический период"), SpaceSim.Units.UnitFormatter.Duration(p.SynodicPeriod), k, v);
                Line(x, ref y, w, Loc.T("Ламберт: импульс через"), SpaceSim.Units.UnitFormatter.Duration(p.Node.Time - now), k, v);
                Line(x, ref y, w, Loc.T("Ламберт: Δv отлёта"), Speed(p.DepartureDeltaV), k, v);
                Line(x, ref y, w, Loc.T("Ламберт: время полёта"), SpaceSim.Units.UnitFormatter.Duration(p.TimeOfFlight), k, v);
                Line(x, ref y, w, Loc.T("v∞ прилёта / захват"), SpaceSim.Units.UnitFormatter.Number(p.ArrivalExcess, 0) + " / " + Speed(p.CaptureDeltaV), k, v);
                Line(x, ref y, w, Loc.T("Всего Δv"), Speed(p.TotalDeltaV), k, v);
            }
            if (!string.IsNullOrEmpty(m.TransferStatus))
            {
                GUI.Label(new Rect(x, y, w, 30f), m.TransferStatus, HudStyle.Small);
                y += 32f;
            }
            float bw = (w - 4f) / 2f;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !m.TransferBusy;
            if (GUI.Button(new Rect(x, y, bw, 26f), m.TransferBusy ? Loc.T("СЧИТАЮ…") : Loc.T("РАССЧИТАТЬ"), HudStyle.Button)) m.PlanTransfer(transferTarget, false);
            if (GUI.Button(new Rect(x + bw + 4f, y, bw, 26f), Loc.T("УЗЕЛ В ОКНЕ"), HudStyle.Primary)) m.PlanTransfer(transferTarget, true);
            GUI.enabled = wasEnabled;
            y += 34f;
            Separator(x, ref y, w);
        }

        static string Short(double seconds)
        {
            long s = (long)Math.Max(0.0, seconds);
            if (s >= 86400) return (s / 86400) + Loc.T("д ") + ((s / 3600) % 24).ToString("00") + Loc.T("ч");
            if (s >= 3600) return (s / 3600) + ":" + ((s / 60) % 60).ToString("00") + ":" + (s % 60).ToString("00");
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        static void Row(float x, ref float y, float w, string key, string value, GUIStyle k, GUIStyle v)
        {
            GUI.Label(new Rect(x, y, w * 0.55f, 30f), key, k);
            GUI.Label(new Rect(x + w * 0.3f, y, w * 0.7f, 30f), value, v);
            HudStyle.HLine(x, y + 30f, w, new Color(1f, 1f, 1f, 0.08f));
            y += 32f;
        }

        void FocusChip(ref float x, ref float y, float left, float width, float h, int index, string text)
        {
            GUIStyle st = HudStyle.Button;
            float w = st.CalcSize(new GUIContent(text)).x + 20f;
            if (x + w > left + width)
            {
                x = left;
                y += h + 6f;
            }
            bool on = index == focus;
            if (GUI.Toggle(new Rect(x, y, w, h), on, text, st) && !on) SetFocus(index);
            x += w + 6f;
        }

        /// <summary>Bottom: key hints in the middle, the line legend on the right.</summary>
        void DrawFooter(Vector2 screen)
        {
            float y = screen.y - 32f, x = screen.x * 0.5f - 420f;
            x += HudStyle.KeyHint(x, y, Loc.T("ЛКМ"), Loc.T("ТОЧКА / УЗЕЛ"));
            x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.WarpToTarget), Loc.T("УСКОРИТЬ ДО ТОЧКИ"));
            x += HudStyle.KeyHint(x, y, Loc.T("КОЛЕСО"), Loc.T("ЗУМ"));
            x += HudStyle.KeyHint(x, y, Loc.T("ПКМ"), Loc.T("ВРАЩЕНИЕ"));
            x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.G), Loc.T("НАВИГАЦИЯ"));
            HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.M), Loc.T("К ПОЛЁТУ"));

            float lx = screen.x - 36f - 640f;
            Legend(ref lx, y, HudStyle.Accent, Loc.T("ТРАЕКТОРИЯ"));
            Legend(ref lx, y, NodeColor, Loc.T("ПОСЛЕ МАНЁВРА (ПУНКТИР)"));
            Legend(ref lx, y, HudStyle.Warning, Loc.T("ЦЕЛЬ"));
            Legend(ref lx, y, OrbitColor, Loc.T("ОРБИТЫ ТЕЛ"));
            GUI.Label(new Rect(screen.x - 36f - 520f, y - 20f, 520f, 14f),
                Loc.T("малые тела показаны крупнее; расстояния — в истинном масштабе"), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextDim, TextAnchor.MiddleRight));
        }

        static void Legend(ref float x, float y, Color c, string label)
        {
            HudStyle.Fill(new Rect(x, y + 10f, 22f, 2f), c);
            GUIStyle s = HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted);
            float w = s.CalcSize(new GUIContent(label)).x;
            GUI.Label(new Rect(x + 28f, y + 2f, w + 4f, 18f), label, s);
            x += 28f + w + 26f;
        }

        void HandleClick()
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || OverUi(e.mousePosition)) return;
            if (boot.Hud != null && boot.Hud.WindowCovers(e.mousePosition)) return;
            int best = int.MinValue;
            float bestDistance = float.MaxValue;
            if (hasVehicle && Vector2.Distance(ToGui(vehicleScreen), e.mousePosition) < 16f)
            {
                best = FocusVehicle;
                bestDistance = Vector2.Distance(ToGui(vehicleScreen), e.mousePosition);
            }
            foreach (MapBody b in bodies)
            {
                if (!b.Visible || b.Screen.z <= 0f) continue;
                float d = Vector2.Distance(ToGui(b.Screen), e.mousePosition);
                if (d < Mathf.Max(16f, b.PixelRadius / HudStyle.Scale) && d < bestDistance)
                {
                    best = b.Index;
                    bestDistance = d;
                }
            }
            // A click right on a body's centre or the vehicle focuses it; anywhere else on the trajectory picks a point there.
            if ((best == int.MinValue || bestDistance > 16f) && boot.Navigation.Maneuvers != null && PickTrajectory(e.mousePosition, out TrajectoryPoint point))
            {
                if (point.Time > boot.Simulation.SimulationTime) boot.Navigation.Maneuvers.Select(point);
                e.Use();
                return;
            }
            if (best == int.MinValue) return;
            SetFocus(best);
            e.Use();
        }
    }
}
