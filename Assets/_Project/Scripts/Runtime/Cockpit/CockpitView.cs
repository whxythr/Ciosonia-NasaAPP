using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The view from the crewed cabin. Zoom the outside camera in to its closest distance and keep pushing the wheel — after a short
    /// fade the camera sits in the pilot's seat (<see cref="CabinRules.ZoomGate"/>); zooming out or the cabin key brings the outside
    /// view back. Only when the vehicle has a crewed cabin (a remote-control probe has nobody inside: the camera simply stops).
    ///
    /// Inside: a procedural interior (<see cref="CockpitInteriorBuilder"/>) hung on the cabin's slot of the rocket model, the hull of
    /// that cabin, the shields round it and the fairing hidden so the windows show the real world; the right mouse button turns the
    /// head within <see cref="CabinRules.ClampHead"/>; the seat load and the engine vibration move the head (<see cref="CabinRules"/>,
    /// from the telemetry — visual only); the panel screens show live altitude, speeds, thrust and propellant. The flight controls
    /// keep working. Never touches the physics.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CockpitView : MonoBehaviour, SpaceCameraController.IInteriorView
    {
        const float FadeOut = 0.22f, FadeIn = 0.35f;
        const float InsideFieldOfView = 72f;
        const float LookSensitivity = 0.15f;

        /// <summary>The player was inside when the flight stack was rebuilt (sphere-of-influence change): sit back in afterwards.</summary>
        static bool resumeInside;

        FlightBody body;
        FlightStateController state;
        SpaceCameraController cameraController;
        SimulationController simulation;
        PlanetRuntime planet;
        Shader lit;
        HelmetVisorOverlay visor;

        readonly CabinRules.ZoomGate gate = new CabinRules.ZoomGate();
        RocketModel builtFor;
        RocketModel.Part cabinPart;
        CockpitInteriorModel interior;
        readonly List<Renderer> hidden = new List<Renderer>();

        bool inside;
        enum Fade { None, ToInside, ToOutside }
        Fade fade = Fade.None;
        float fadeAlpha, fadeClock;
        bool fadeSwitched;
        float outsideFov = -1f;

        double headYaw, headPitch;
        float sag, shakeAmplitude, strain;
        Vector3 shake;
        GUIStyle screenStyle;

        public bool Inside => inside;
        public bool Busy => fade != Fade.None;
        public float HeadYaw => (float)headYaw;
        public float HeadPitch => (float)headPitch;
        public CockpitInterior InteriorKind => interior != null ? interior.Kind : CockpitInterior.None;
        /// <summary>Module id of the cabin the player sits in (or would sit in), "" if the vehicle has none.</summary>
        public string CabinId
        {
            get
            {
                PartPlacement p = FindCabin();
                return p != null ? p.ModuleId : "";
            }
        }

        public void Initialize(FlightBody flightBody, FlightStateController flightState, SpaceCameraController cam, SimulationController sim,
            PlanetRuntime planetRuntime, Shader litShader)
        {
            body = flightBody;
            state = flightState;
            cameraController = cam;
            simulation = sim;
            planet = planetRuntime;
            lit = litShader;
            visor = gameObject.AddComponent<HelmetVisorOverlay>();
        }

        void Start()
        {
            if (!resumeInside) return;
            resumeInside = false;
            if (CanEnter(out _)) SwitchInside();
        }

        void OnDestroy()
        {
            // A flight-stack rebuild in the middle of a flight (new sphere of influence) brings the player back in afterwards.
            resumeInside = inside && body != null && !body.Destroyed;
            LeaveNow(false);
            interior?.Destroy();
            interior = null;
        }

        // ---- Rules -------------------------------------------------------------------------------------------

        PartPlacement FindCabin()
        {
            RocketModel m = body != null ? body.Model : null;
            return m != null && m.Layout != null ? CabinRules.CrewedCabin(m.Layout.Parts) : null;
        }

        /// <summary>Why the player cannot sit in the cabin now (false + reason), or true.</summary>
        public bool CanEnter(out string reason)
        {
            reason = "";
            if (body == null || body.Simulation == null || cameraController == null) reason = Loc.T("нет аппарата");
            else if (body.Destroyed) reason = Loc.T("аппарат разрушен");
            else if (body.VisualHidden) reason = Loc.T("ракета в ангаре");
            else if (body.Model == null || FindCabin() == null) reason = Loc.T("на аппарате нет пилотируемой кабины: зонд управляется по радио");
            else if (cameraController.FirstPerson != null && !ReferenceEquals(cameraController.FirstPerson, this)) reason = Loc.T("камера занята (выход на поверхность или ангар)");
            else if (cameraController.Focus != SpaceCameraController.FocusTarget.FlightBody) reason = Loc.T("камера смотрит на планету");
            return reason.Length == 0;
        }

        /// <summary>Goes inside (with the fade unless <paramref name="instant"/>); returns the reason when it cannot.</summary>
        public string Enter(bool instant = false)
        {
            if (inside) return "";
            if (!CanEnter(out string reason)) return reason;
            if (instant) SwitchInside();
            else StartFade(Fade.ToInside);
            return "";
        }

        public void Exit(bool instant = false)
        {
            if (!inside) return;
            if (instant) LeaveNow(true);
            else StartFade(Fade.ToOutside);
        }

        /// <summary>Turns the head [deg, yaw + right, pitch + up] within the limits (as the right mouse button would).</summary>
        public void SetHead(double yawDegrees, double pitchDegrees)
        {
            headYaw = yawDegrees;
            headPitch = pitchDegrees;
            CabinRules.ClampHead(ref headYaw, ref headPitch);
        }

        public void Toggle()
        {
            if (Busy) return;
            if (inside)
            {
                Exit();
                return;
            }
            string reason = Enter();
            if (reason.Length > 0 && state != null) state.Notify(Loc.T("Вид из кабины недоступен: ") + reason + ".");
        }

        void StartFade(Fade direction)
        {
            fade = direction;
            fadeClock = 0f;
            fadeSwitched = false;
        }

        // ---- Switching -----------------------------------------------------------------------------------

        void SwitchInside()
        {
            PartPlacement cabin = FindCabin();
            RocketModel model = body.Model;
            RocketModel.Part part = cabin != null ? model.Find(cabin) : null;
            if (part == null || part.Transform == null) return;
            if (interior == null || builtFor != model || cabinPart != part || interior.Root == null)
            {
                interior?.Destroy();
                ModuleDefinition m = ModuleCatalog.Find(cabin.ModuleId);
                interior = CockpitInteriorBuilder.Build(part.Transform, CabinRules.InteriorOf(m), (float)cabin.Diameter, (float)cabin.Height, lit);
                builtFor = model;
                cabinPart = part;
            }
            // Hide the hull of this cabin, the shields round it and the fairing: they would cover the windows.
            hidden.Clear();
            foreach (RocketModel.Part p in model.Parts)
            {
                if (!CabinRules.HiddenFromInside(p.Placement, cabin) || p.Renderers == null) continue;
                foreach (Renderer r in p.Renderers)
                {
                    if (r == null || !r.enabled) continue;
                    r.enabled = false;
                    hidden.Add(r);
                }
            }
            interior.Active = true;
            inside = true;
            headYaw = headPitch = 0.0;
            sag = 0f;
            gate.Reset();
            cameraController.FirstPerson = this;
            Camera cam = cameraController.Camera;
            if (cam != null)
            {
                outsideFov = cam.fieldOfView;
                cam.fieldOfView = InsideFieldOfView;
            }
        }

        /// <summary>Back to the outside camera: hull shown again, interior off, the orbit camera a little way out.</summary>
        void LeaveNow(bool moveCamera)
        {
            foreach (Renderer r in hidden)
                if (r != null) r.enabled = true;
            hidden.Clear();
            if (interior != null) interior.Active = false;
            if (visor != null) visor.Visible = false;
            bool was = inside;
            inside = false;
            fade = Fade.None;
            fadeAlpha = 0f;
            gate.Reset();
            if (cameraController == null) return;
            if (ReferenceEquals(cameraController.FirstPerson, this)) cameraController.FirstPerson = null;
            if (cameraController.Camera != null && outsideFov > 0f) cameraController.Camera.fieldOfView = outsideFov;
            outsideFov = -1f;
            if (was && moveCamera)
                cameraController.SetView(cameraController.Yaw, cameraController.Pitch, cameraController.MinDistance * CabinRules.ExitDistanceFactor);
        }

        // ---- Frame loop ----------------------------------------------------------------------------------

        void Update()
        {
            if (body == null || body.Simulation == null || cameraController == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (inside) KeepValid();
            AdvanceFade(dt);

            bool input = !FlightInput.IsTyping && !FlightInput.MapOpen && fade == Fade.None;
            // On the surface the same key switches the robot's first / third person view (SurfaceExplorer), not the cabin.
            bool eva = cameraController.FirstPerson is SurfaceExplorer;
            if (input && !eva && FlightInput.GetKeyDown(InputKey.T)) Toggle();
            // The hangar takes the camera too: step out first, so its key works from the cabin.
            if (inside && input && body.IsOnLaunchPad && FlightInput.GetKeyDown(InputKey.B)) LeaveNow(true);

            if (inside)
            {
                if (input) Look();
                Animate(dt);
            }
            else if (input && cameraController.FirstPerson == null) WatchZoom();
        }

        /// <summary>Destroyed, rebuilt or taken over (EVA, hangar): the outside view comes back.</summary>
        void KeepValid()
        {
            string reason = null;
            if (body.Destroyed) reason = Loc.T("Кабина разрушена — вид снаружи.");
            else if (body.VisualHidden) reason = "";
            else if (body.Model != builtFor || body.Model == null || FindCabin() == null) reason = "";
            else if (!ReferenceEquals(cameraController.FirstPerson, this)) reason = "";
            if (reason == null) return;
            bool takenOver = cameraController.FirstPerson != null && !ReferenceEquals(cameraController.FirstPerson, this);
            if (takenOver)
            {
                // Someone else (the astronaut going outside) owns the camera now: just restore the rocket, leave the camera alone.
                RestoreQuietly();
            }
            else LeaveNow(true);
            if (reason.Length > 0 && state != null) state.Notify(reason);
        }

        void RestoreQuietly()
        {
            foreach (Renderer r in hidden)
                if (r != null) r.enabled = true;
            hidden.Clear();
            if (interior != null) interior.Active = false;
            if (visor != null) visor.Visible = false;
            inside = false;
            fade = Fade.None;
            fadeAlpha = 0f;
            outsideFov = -1f; // the new owner keeps whatever field of view it set
        }

        void WatchZoom()
        {
            if (cameraController.Focus != SpaceCameraController.FocusTarget.FlightBody) return;
            float notches = FlightInput.ScrollNotches;
            if (FlightInput.GetKey(InputKey.Equals)) notches += 20f * Time.unscaledDeltaTime;
            if (FlightInput.GetKey(InputKey.Minus)) notches -= 20f * Time.unscaledDeltaTime;
            bool atMin = CabinRules.AtMinimum(cameraController.TargetDistance, cameraController.MinDistance);
            if (!gate.Update(atMin, notches, Time.unscaledTime)) return;
            if (FindCabin() == null) return; // a remote-control probe: the camera simply stops at its closest
            Enter();
        }

        void Look()
        {
            if (FlightInput.RightMouseHeld || FlightInput.MiddleMouseHeld)
            {
                Vector2 d = FlightInput.LookDelta;
                headYaw += d.x * LookSensitivity;
                headPitch -= d.y * LookSensitivity;
                CabinRules.ClampHead(ref headYaw, ref headPitch);
            }
            float notches = FlightInput.ScrollNotches;
            if (FlightInput.GetKey(InputKey.Minus)) notches -= 1f;
            if (notches < -0.01f) Exit();
        }

        void AdvanceFade(float dt)
        {
            if (fade == Fade.None) return;
            fadeClock += dt;
            if (!fadeSwitched)
            {
                fadeAlpha = Mathf.Clamp01(fadeClock / FadeOut);
                if (fadeClock < FadeOut) return;
                fadeSwitched = true;
                fadeClock = 0f;
                if (fade == Fade.ToInside)
                {
                    if (CanEnter(out string reason)) SwitchInside();
                    else if (state != null) state.Notify(Loc.T("Вид из кабины недоступен: ") + reason + ".");
                }
                else LeaveNow(true);
                return;
            }
            fadeAlpha = 1f - Mathf.Clamp01(fadeClock / FadeIn);
            if (fadeClock >= FadeIn)
            {
                fade = Fade.None;
                fadeAlpha = 0f;
            }
        }

        /// <summary>Head sag under the seat load, vibration from the engine and the air, the helmet's tunnel vision at high g.</summary>
        void Animate(float dt)
        {
            FlightTelemetry t = state != null ? state.Telemetry : null;
            var rocket = body.Rocket;
            double load = t != null ? CabinRules.SeatLoad(t.IsLanded, t.GravityMagnitude, t.ThrustAcceleration, t.DragAcceleration) : 1.0;
            bool paused = simulation != null && simulation.Paused;
            float k = 1f - Mathf.Exp(-6f * dt);
            sag = Mathf.Lerp(sag, (float)CabinRules.HeadSag(load), k);
            float amplitude = paused || t == null ? 0f : (float)CabinRules.ShakeAmplitude(rocket != null && rocket.IsBurning, t.ThrustAcceleration, t.DynamicPressure);
            shakeAmplitude = Mathf.Lerp(shakeAmplitude, amplitude, 1f - Mathf.Exp(-10f * dt));
            float time = Time.unscaledTime;
            shake = new Vector3(
                shakeAmplitude * (0.6f * Mathf.Sin(time * 71f) + 0.4f * Mathf.Sin(time * 113f + 1.3f)),
                shakeAmplitude * (0.6f * Mathf.Sin(time * 89f + 0.7f) + 0.4f * Mathf.Sin(time * 131f + 2.1f)),
                shakeAmplitude * 0.5f * Mathf.Sin(time * 97f + 0.4f));
            strain = Mathf.Lerp(strain, Mathf.Clamp01((float)((load - 3.0) / 5.0)) * 0.8f, k);
            if (visor != null)
            {
                visor.Visible = true;
                visor.Strain = strain;
            }
        }

        // ---- First-person view (called by the camera) -------------------------------------------------------

        public void GetView(double renderTime, out Vector3d eye, out Vector3d forward, out Vector3d up)
        {
            var sim = body.Simulation;
            var pm = planet.Model;
            double alpha = simulation != null ? simulation.InterpolationAlpha : 1.0;
            Vector3d p = body.RenderPosition(alpha, renderTime);
            Vector3d radial = (p - pm.Position).Normalized;
            // The rocket's attitude exactly as FlightBody draws it this frame (vertical on the ground, the commanded one in flight).
            Vector3d axis = sim.IsLanded || sim.Rocket == null ? radial : sim.ThrustDirection(p);
            Vector3 axisU = CoordinateSystem.ToUnityVector(axis);
            Vector3 fwd = Vector3.ProjectOnPlane(CoordinateSystem.ToUnityVector(pm.Rotation.SpinAxis), axisU);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.Cross(axisU, Vector3.right);
            Quaternion rocket = Quaternion.LookRotation(fwd.normalized, axisU);

            Transform bodyT = body.transform;
            Transform eyeT = interior != null ? interior.Eye : null;
            if (eyeT == null)
            {
                eye = p;
                forward = CoordinateSystem.FromUnityVector(rocket * Vector3.forward);
                up = axis;
                return;
            }
            // The eye in the rocket's own frame (the transforms of the model are consistent with each other whatever the origin).
            Vector3 local = bodyT.InverseTransformPoint(eyeT.position + eyeT.rotation * new Vector3(0f, -sag, 0f));
            Quaternion seat = Quaternion.Inverse(bodyT.rotation) * eyeT.rotation;
            Quaternion head = Quaternion.Euler((float)-headPitch + shake.x + sag * 50f, (float)headYaw + shake.y, shake.z);
            Quaternion look = rocket * seat * head;
            eye = p + CoordinateSystem.FromUnityVector(rocket * local);
            forward = CoordinateSystem.FromUnityVector(look * Vector3.forward).Normalized;
            up = CoordinateSystem.FromUnityVector(look * Vector3.up).Normalized;
        }

        // ---- Panel screens and the fade ---------------------------------------------------------------------

        void OnGUI()
        {
            if (FlightInput.PauseMenuOpen) return;
            Matrix4x4 old = GUI.matrix;
            if (inside && !FlightInput.MapOpen && interior != null && cameraController != null && cameraController.Camera != null) DrawScreens();
            if (fadeAlpha > 0.001f)
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
                GUI.depth = -50;
                Color c = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, fadeAlpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = c;
            }
            GUI.matrix = old;
        }

        void DrawScreens()
        {
            HudStyle.Begin();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
            GUI.depth = 20; // under the HUD panels
            if (screenStyle == null) screenStyle = new GUIStyle(HudStyle.Mono) { wordWrap = false, richText = true, alignment = TextAnchor.UpperLeft };
            Camera cam = cameraController.Camera;
            foreach (CockpitInteriorModel.Screen s in interior.Screens)
            {
                if (s.Transform == null) continue;
                if (!ScreenRect(cam, s, out Rect r)) continue;
                string[] lines = ScreenLines(s.Index);
                int size = Mathf.Clamp(Mathf.RoundToInt(r.height / (lines.Length + 0.8f)), 6, 40);
                if (size < 7) continue; // too small to read: looking away
                screenStyle.fontSize = size;
                screenStyle.normal.textColor = new Color(0.55f, 0.95f, 0.85f);
                float pad = r.height * 0.06f;
                var inner = new Rect(r.x + pad, r.y + pad, r.width - 2f * pad, r.height - 2f * pad);
                GUI.Label(inner, string.Join("\n", lines), screenStyle);
            }
        }

        /// <summary>Screen-space bounds of a panel screen (false if it is behind the eye).</summary>
        static bool ScreenRect(Camera cam, CockpitInteriorModel.Screen s, out Rect rect)
        {
            rect = default;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int c = 0; c < 4; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? -0.5f : 0.5f, (c & 2) == 0 ? -0.5f : 0.5f, 0f);
                Vector3 world = s.Transform.TransformPoint(corner);
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0.01f) return false;
                float y = Screen.height - sp.y;
                minX = Mathf.Min(minX, sp.x);
                maxX = Mathf.Max(maxX, sp.x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            if (maxX < 0f || minX > Screen.width || maxY < 0f || minY > Screen.height) return false;
            rect = new Rect(minX, minY, maxX - minX, maxY - minY);
            return rect.width > 4f && rect.height > 4f;
        }

        /// <summary>Live readouts from the telemetry (Core) — the panel only formats them.</summary>
        string[] ScreenLines(int index)
        {
            FlightTelemetry t = state != null ? state.Telemetry : null;
            var r = body.Rocket;
            if (t == null) return new[] { "—" };
            switch (index)
            {
                case 0:
                    return new[]
                    {
                        Loc.T("ВЫСОТА"),
                        Distance(t.AltitudeAboveSurface),
                        Loc.T("ВЕРТ. СКОР."),
                        Signed(t.VelocityUp) + Loc.T(" м/с"),
                        Loc.T("СКОР. ") + UnitFormatter.Number(t.SurfaceSpeed, 0) + Loc.T(" м/с"),
                    };
                case 1:
                {
                    double thrust = r != null ? t.ThrustAcceleration * r.Mass : 0.0;
                    double load = CabinRules.SeatLoad(t.IsLanded, t.GravityMagnitude, t.ThrustAcceleration, t.DragAcceleration);
                    string engine = r == null || !r.HasStages ? Loc.T("нет двигателя")
                        : r.IsBurning ? Loc.T("ДВИГ. РАБОТАЕТ") : Loc.T("двиг. выключен");
                    return new[]
                    {
                        Loc.T("ТЯГА"),
                        UnitFormatter.Number(thrust / 1000.0, thrust < 1e4 ? 2 : 0) + Loc.T(" кН"),
                        r != null && r.HasStages ? Loc.T("дроссель ") + UnitFormatter.Number(r.Throttle * 100.0, 0) + " %" : "",
                        Loc.T("ПЕРЕГР. ") + UnitFormatter.Number(load, 1) + " g",
                        engine,
                    };
                }
                default:
                {
                    if (r == null || !r.HasStages) return new[] { Loc.T("ТОПЛИВО"), "—", Loc.T("ступеней нет") };
                    double capacity = r.CurrentStage.propellantMass;
                    double fraction = capacity > 0.0 ? r.CurrentPropellant / capacity : 0.0;
                    return new[]
                    {
                        Loc.T("ТОПЛИВО"),
                        UnitFormatter.Number(r.CurrentPropellant / 1000.0, r.CurrentPropellant < 1e4 ? 2 : 1) + Loc.T(" т"),
                        UnitFormatter.Number(fraction * 100.0, 0) + Loc.T(" % ступени"),
                        Loc.T("ступень ") + (r.StageIndex + 1) + " / " + r.StageCount,
                        "Δv " + UnitFormatter.Number(r.DeltaVRemaining(), 0) + Loc.T(" м/с"),
                    };
                }
            }
        }

        static string Distance(double metres) =>
            System.Math.Abs(metres) >= 10000.0 ? UnitFormatter.Number(metres / 1000.0, 1) + Loc.T(" км") : UnitFormatter.Number(metres, 0) + Loc.T(" м");

        static string Signed(double v) => (v >= 0.0 ? "+" : "") + UnitFormatter.Number(v, 1);
    }
}
