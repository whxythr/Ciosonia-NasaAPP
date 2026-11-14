using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        TrajectoryPrediction shownPrediction;

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
        static void FillLine(MapLine line, IList<Vector3d> points)
        {
            double norm = 1.0;
            foreach (Vector3d p in points) norm = Math.Max(norm, p.Magnitude);
            line.Norm = norm;
            int segments = points.Count - 1;
            var vertices = new Vector3[segments * 4];
            var other = new Vector2[segments * 4];
            var extra = new Vector2[segments * 4];
            var triangles = new int[segments * 6];
            for (int s = 0; s < segments; s++)
            {
                Vector3 a = CoordinateSystem.ToUnityVector(points[s] / norm), b = CoordinateSystem.ToUnityVector(points[s + 1] / norm);
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

        void RefreshTrajectory()
        {
            TrajectoryPrediction prediction = boot.Navigation.Prediction;
            if (ReferenceEquals(prediction, shownPrediction)) return;
            shownPrediction = prediction;
            int used = 0;
            if (prediction != null)
            {
                foreach (PredictedPatch patch in prediction.Patches)
                {
                    if (patch.RelativePoints.Count < 2) continue;
                    if (used == trajectory.Count) trajectory.Add(NewLine("MapTrajectory", HudStyle.Accent, 1.25f));
                    MapLine line = trajectory[used++];
                    line.Anchor = patch.Primary;
                    FillLine(line, patch.RelativePoints);
                    line.Go.SetActive(true);
                }
            }
            for (int i = used; i < trajectory.Count; i++) trajectory[i].Go.SetActive(false);
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
        /// The right panel (mock-up "Trans-Tromos injection"): the planned manoeuvre with the time to ignition and its Δv in big type,
        /// or — without a plan — the current orbit; then the bodies to centre the map on.
        /// </summary>
        void DrawPanel(Vector2 screen)
        {
            const float w = 420f;
            var nav = boot.Navigation;
            NodeGuidance g = nav.Guidance;
            float x = screen.x - 36f - w, y = 120f;
            float h = Mathf.Min(screen.y - 120f - 100f, 760f);
            var r = Ui(new Rect(x, y, w, h));
            HudStyle.Card(r, 0.9f);
            float ix = x + 22f, iw = w - 44f, cy = y + 20f;
            GUIStyle k = HudStyle.Style(HudStyle.Face.Body, 13, HudStyle.TextPanel), v = HudStyle.Style(HudStyle.Face.Mono, 13, HudStyle.TextStrong, TextAnchor.MiddleRight);
            int target = nav.Plan.Target;
            string targetName = target >= 0 && nav.System != null ? Loc.T(nav.System.NameOf(target)) : "";

            if (g.Active)
            {
                GUI.Label(new Rect(ix, cy, iw, 14f), Loc.T("МАНЁВР 01") + "  ·  " + (g.Started ? Loc.T("ИДЁТ") : Loc.T("ЗАПЛАНИРОВАН")), HudStyle.Style(HudStyle.Face.Mono, 11, HudStyle.Blue));
                cy += 20f;
                string title = targetName.Length > 0 ? Loc.T("Перелёт к ") + targetName : Loc.T("Манёвр");
                GUI.Label(new Rect(ix, cy, iw, 30f), title, HudStyle.Style(HudStyle.Face.Strong, 24, HudStyle.TextStrong));
                cy += 44f;
                GUI.Label(new Rect(ix, cy, 200f, 14f), Loc.T("ДО ЗАЖИГАНИЯ"), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
                GUI.Label(new Rect(ix + 220f, cy, 150f, 14f), Loc.T("Δv, М/С"), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
                string burnIn = g.Started ? Loc.T("ИДЁТ") : g.TimeToIgnition > 0 ? Short(g.TimeToIgnition) : Loc.T("СЕЙЧАС");
                GUI.Label(new Rect(ix - 2f, cy + 12f, 220f, 66f), burnIn, HudStyle.Style(HudStyle.Face.DisplayStrong, 56, HudStyle.Blue));
                GUI.Label(new Rect(ix + 218f, cy + 12f, 170f, 66f), SpaceSim.Units.UnitFormatter.Number(g.Remaining, 0), HudStyle.Style(HudStyle.Face.DisplayStrong, 56, HudStyle.TextStrong));
                cy += 88f;
                Row(ix, ref cy, iw, Loc.T("Работа двигателя"), SpaceSim.Units.UnitFormatter.Number(g.BurnTime, 0) + Loc.T(" с"), k, v);
                Row(ix, ref cy, iw, Loc.T("Тангаж / курс"), SpaceSim.Units.UnitFormatter.Number(g.Pitch, 1) + "° / " + SpaceSim.Units.UnitFormatter.Number(g.Heading, 1) + "°", k, v);
                SpaceSim.Navigation.TrajectoryPrediction p = nav.Prediction;
                if (p != null && target >= 0)
                    Row(ix, ref cy, iw, targetName, p.Encounter ? Loc.T("встреча через ") + SpaceSim.Units.UnitFormatter.Duration(p.EncounterTime - nav.Now)
                        : Loc.T("промах ") + SpaceSim.Units.UnitFormatter.Number(p.ClosestDistance / 1e9, 2) + Loc.T(" млн км"), k, v);
                if (orbit != null)
                {
                    Row(ix, ref cy, iw, Loc.T("Перицентр сейчас"), orbit.PeriapsisText, k, v);
                    Row(ix, ref cy, iw, Loc.T("Апоцентр сейчас"), orbit.ApoapsisText, k, v);
                }
                cy += 14f;
                if (GUI.Button(new Rect(ix, cy, 120f, 40f), Loc.T("УДАЛИТЬ"), HudStyle.BigButton))
                {
                    nav.CancelWarpToBurn();
                    nav.ClearNode();
                }
                bool warping = nav.WarpingToBurn;
                bool canWarp = !g.Started && g.TimeToIgnition > NavigationService.WarpToBurnLead;
                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && (canWarp || warping);
                if (GUI.Button(new Rect(ix + 132f, cy, iw - 132f, 40f), warping ? Loc.T("ОСТАНОВИТЬ УСКОРЕНИЕ") : Loc.T("УСКОРИТЬ ДО МАНЁВРА") + "  →", HudStyle.Primary))
                {
                    if (warping) nav.CancelWarpToBurn();
                    else nav.WarpToBurn();
                }
                GUI.enabled = wasEnabled;
                cy += 50f;
                GUI.Label(new Rect(ix, cy, iw, 30f), Loc.T("Время ускоряется и само вернётся к ×1 за ") + NavigationService.WarpToBurnLead.ToString("0") +
                                                    Loc.T(" с до зажигания. Двигатель включаете вы."), HudStyle.Small);
                cy += 36f;
            }
            else
            {
                GUI.Label(new Rect(ix, cy, iw, 14f), orbit != null ? Loc.T("ОРБИТА · ВОКРУГ ") + orbit.PrimaryName.ToUpperInvariant() : Loc.T("ОРБИТА"),
                    HudStyle.Style(HudStyle.Face.Mono, 11, HudStyle.Blue));
                cy += 22f;
                if (orbit == null)
                {
                    GUI.Label(new Rect(ix, cy, iw, 40f), Loc.T("Аппарат на поверхности — орбиты нет."), HudStyle.Body);
                    cy += 44f;
                }
                else
                {
                    GUI.Label(new Rect(ix, cy, 200f, 14f), Loc.T("ПЕРИЦЕНТР"), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
                    GUI.Label(new Rect(ix + 200f, cy, 170f, 14f), Loc.T("АПОЦЕНТР"), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
                    GUI.Label(new Rect(ix, cy + 14f, 196f, 32f), orbit.PeriapsisText, HudStyle.Style(HudStyle.Face.Mono, 18, orbit.PeriapsisBelowSurface ? HudStyle.Danger : HudStyle.Blue));
                    GUI.Label(new Rect(ix + 200f, cy + 14f, 176f, 32f), orbit.ApoapsisText, HudStyle.Style(HudStyle.Face.Mono, 18, HudStyle.TextStrong));
                    cy += 54f;
                    Row(ix, ref cy, iw, Loc.T("До перицентра"), orbit.TimeToPeriapsisText, k, v);
                    Row(ix, ref cy, iw, Loc.T("До апоцентра"), orbit.TimeToApoapsisText, k, v);
                    Row(ix, ref cy, iw, Loc.T("Период"), orbit.PeriodText, k, v);
                    Row(ix, ref cy, iw, Loc.T("Эксцентриситет"), SpaceSim.Units.UnitFormatter.Number(orbit.Apsides.Eccentricity, 4), k, v);
                }
                GUI.Label(new Rect(ix, cy + 4f, iw, 34f), Loc.T("Высоты — над средним радиусом тела. Манёвр планируется в навигации (") +
                                                         FlightInput.KeyLabel(InputKey.G) + ").", HudStyle.Small);
                cy += 44f;
            }

            // Bodies to centre the map on.
            GUI.Label(new Rect(ix, cy, iw, 14f), Loc.T("ПОКАЗАТЬ"), HudStyle.Style(HudStyle.Face.Mono, 11, HudStyle.TextSoft));
            cy += 20f;
            float bx = ix, rowH = 28f;
            FocusChip(ref bx, ref cy, ix, iw, rowH, FocusVehicle, Loc.T("Аппарат"));
            foreach (MapBody b in bodies)
            {
                if (cy + rowH > r.yMax - 8f) break;
                FocusChip(ref bx, ref cy, ix, iw, rowH, b.Index, Loc.T(b.Name));
            }
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
            float y = screen.y - 32f, x = screen.x * 0.5f - 300f;
            x += HudStyle.KeyHint(x, y, Loc.T("КОЛЕСО"), Loc.T("ЗУМ"));
            x += HudStyle.KeyHint(x, y, Loc.T("ПКМ"), Loc.T("ВРАЩЕНИЕ"));
            x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.G), Loc.T("НАВИГАЦИЯ"));
            HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.M), Loc.T("К ПОЛЁТУ"));

            float lx = screen.x - 36f - 520f;
            Legend(ref lx, y, HudStyle.Accent, Loc.T("ТРАЕКТОРИЯ"));
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
            if (best == int.MinValue) return;
            SetFocus(best);
            e.Use();
        }
    }
}
