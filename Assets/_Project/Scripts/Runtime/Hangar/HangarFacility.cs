using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using System.Text;
using SpaceSim.Mathematics;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Hangar mode: the rocket leaves the pad for the assembly hall. The pad rocket is hidden — physics is untouched, it stays landed
    /// where it was — while the design being edited stands on the stand, built by <see cref="RocketModelBuilder"/> (the same code
    /// that draws the flying rocket), and the camera orbits it inside the hall. Every structural edit rebuilds the preview, so
    /// installed modules appear in their slots at once; parts can be hovered and clicked. Leaving with "apply" puts the new rocket on
    /// the pad through <see cref="FlightBody.ApplyBlueprint"/> (done by <see cref="HangarUI"/>). Purely visual.
    /// </summary>
    [DefaultExecutionOrder(190)]
    public sealed class HangarFacility : MonoBehaviour, SpaceCameraController.IFirstPersonView
    {
        const float EyeMargin = 2.5f;
        /// <summary>The camera stays under the crane bridge.</summary>
        const float CeilingClearance = 14f;
        const float MinDistance = 3f, MaxDistance = 75f;
        const float OrbitDegreesPerPixel = 0.15f;
        static readonly Color HighlightColor = new Color(0.50f, 0.83f, 1f);

        FlightBody body;
        HangarBuilding building;
        SpaceCameraController cameraController;
        RocketPalette palette;
        RocketModel preview;
        string shownSignature = "";
        bool inside, showFairing;

        float yaw = 32f, pitch = 6f, targetYaw = 32f, targetPitch = 6f;
        float distance = 55f, targetDistance = 55f;
        float focus = 25f, targetFocus = 25f;

        RocketModel.Part hovered, selected, flashed;
        float flashUntil;
        readonly Dictionary<RocketModel.Part, float> tinted = new Dictionary<RocketModel.Part, float>();

        public bool Inside => inside;
        public RocketModel Preview => preview;
        public HangarBuilding Building => building;
        public RocketModel.Part Hovered => hovered;
        public RocketModel.Part Selected => selected;
        /// <summary>Set by the hangar window every frame: the pointer is over a panel (no orbiting, zooming or picking then).</summary>
        public bool PointerOverUi { get; set; }
        /// <summary>
        /// Set by the hangar window while a part is dragged from the catalog: every part of this stage is lit (−1 = the head section,
        /// null = none), so the player sees where the part will go.
        /// </summary>
        public int? HighlightStage { get; set; }
        /// <summary>A part of the preview was clicked in the 3D view.</summary>
        public event Action<RocketModel.Part> PartClicked;
        /// <summary>Camera: azimuth around the rocket [deg, 0 = from the gate side], elevation [deg], distance [m], aim height above the stand [m].</summary>
        public float Yaw => yaw;
        public float Pitch => pitch;
        public float Distance => distance;
        public float FocusHeight => focus;

        /// <summary>The nose fairing hides the payload; in the hangar it is off unless the player wants to see it.</summary>
        public bool ShowFairing
        {
            get => showFairing;
            set
            {
                showFairing = value;
                if (preview != null && preview.Fairing != null) preview.Fairing.SetActive(value);
            }
        }

        public void Initialize(FlightBody flightBody, HangarBuilding hangarBuilding, SpaceCameraController cam, Shader lit)
        {
            body = flightBody;
            building = hangarBuilding;
            cameraController = cam;
            palette = new RocketPalette(lit);
        }

        void OnDestroy()
        {
            // The camera outlives the flight stack: never leave it looking through a destroyed hangar. (The pad rocket is not shown
            // again here: it is destroyed together with this flight stack.)
            if (cameraController != null && ReferenceEquals(cameraController.FirstPerson, this)) cameraController.FirstPerson = null;
            if (cameraController != null && cameraController.Camera != null && outsideFieldOfView > 0f)
                cameraController.Camera.fieldOfView = outsideFieldOfView;
            preview?.Destroy();
            palette?.Dispose();
        }

        /// <summary>Why the rocket cannot be taken into the hangar right now (empty when it can).</summary>
        public bool CanEnter(out string reason)
        {
            reason = "";
            if (body == null || body.Simulation == null || building == null) reason = Loc.T("нет ракеты или ангара");
            else if (body.Destroyed) reason = Loc.T("ракета разбита — ") + FlightInput.KeyLabel(InputKey.R) + Loc.T(": новая ракета на старте");
            else if (!body.IsOnLaunchPad) reason = Loc.T("ракета не на стартовой площадке");
            else if (body.Rocket != null && body.Rocket.IsBurning) reason = Loc.T("работает двигатель");
            else if (cameraController != null && cameraController.FirstPerson != null && !ReferenceEquals(cameraController.FirstPerson, this))
                reason = Loc.T("астронавт снаружи — сначала вернитесь в ракету");
            return reason.Length == 0;
        }

        /// <summary>Takes the rocket into the hall: the pad rocket disappears, the design stands on the stand, the camera goes inside.</summary>
        public void Enter(RocketBlueprint design)
        {
            inside = true;
            body.VisualHidden = true;
            building.SetLights(true);
            cameraController.FirstPerson = this;
            // A wider lens inside: the hall is only ~57 m across its diagonal, a tall rocket must still fit in the picture.
            Camera cam = cameraController.Camera;
            if (cam != null)
            {
                outsideFieldOfView = cam.fieldOfView;
                cam.fieldOfView = HallFieldOfView;
            }
            hovered = selected = flashed = null;
            ShowDesign(design, true);
            FrameAll(true);
        }

        /// <summary>Back to the pad view (the caller has already applied the design or dropped it).</summary>
        public void Exit()
        {
            inside = false;
            HighlightStage = null;
            ClearPreview();
            hovered = selected = flashed = null;
            if (building != null) building.SetLights(false);
            if (cameraController != null && ReferenceEquals(cameraController.FirstPerson, this)) cameraController.FirstPerson = null;
            if (cameraController != null && cameraController.Camera != null && outsideFieldOfView > 0f)
                cameraController.Camera.fieldOfView = outsideFieldOfView;
            outsideFieldOfView = 0f;
            if (body != null) body.VisualHidden = false;
        }

        const float HallFieldOfView = 72f;
        float outsideFieldOfView;

        // ---- Preview ----------------------------------------------------------------------------------------

        /// <summary>Shows <paramref name="design"/> on the stand. Rebuilt only when its parts change (not for the fuel load).</summary>
        public void ShowDesign(RocketBlueprint design, bool force = false)
        {
            if (design == null || body == null || building == null) return;
            string signature = Signature(design);
            if (!force && preview != null && signature == shownSignature) return;
            string selectedKey = selected != null ? Key(selected.Placement) : null;
            ClearPreview();
            shownSignature = signature;
            double radius = body.Compiled != null ? body.Compiled.Configuration.radius : RocketLayout.DefaultRadius;
            preview = RocketModelBuilder.Build(design, radius, body.OwnsModule, building.Bay, palette, ModuleVisualLibrary.Active);
            preview.SetWingsDeployed(false);
            preview.Fairing.SetActive(showFairing);
            selected = selectedKey != null ? FindByKey(selectedKey) : null;
            hovered = flashed = null;
            targetFocus = Mathf.Clamp(targetFocus, 0f, (float)preview.Layout.Height + 2f);
        }

        void ClearPreview()
        {
            tinted.Clear();
            preview?.Destroy();
            preview = null;
            shownSignature = "";
        }

        /// <summary>What the preview depends on: parts, counts and which shop modules are owned — not the fuel load.</summary>
        string Signature(RocketBlueprint design)
        {
            var sb = new StringBuilder();
            void Id(string id)
            {
                sb.Append(id);
                ModuleDefinition m = ModuleCatalog.Find(id);
                if (m != null) sb.Append(body.OwnsModule(id) ? "+" : "-"); // any part can be locked in a Career
                sb.Append(',');
            }
            foreach (StageBlueprint s in design.stages)
            {
                sb.Append('|');
                Id(s.engineId ?? "");
                sb.Append(s.engineCount).Append(':');
                foreach (TankLoad t in s.tanks) Id(t.moduleId);
                sb.Append("/A:");
                if (s.attachments != null) foreach (string a in s.attachments) Id(a);
            }
            sb.Append("|P:");
            foreach (string id in design.payloadModules) Id(id);
            return sb.ToString();
        }

        static string Key(PartPlacement p) => p.Role + "/" + p.Stage + "/" + p.Index + "/" + p.ModuleId;

        RocketModel.Part FindByKey(string key)
        {
            if (preview == null) return null;
            foreach (RocketModel.Part p in preview.Parts)
                if (Key(p.Placement) == key) return p;
            return null;
        }

        /// <summary>Selects a part as if it had been clicked (null clears the selection).</summary>
        public void Select(RocketModel.Part part) => selected = part;

        // ---- Camera -----------------------------------------------------------------------------------------

        /// <summary>
        /// Aims at the middle of the rocket from far enough to see all of it (as far as the hall allows): from the hall's diagonal,
        /// the longest view inside, with room for the panels above and below the picture.
        /// </summary>
        public void FrameAll(bool immediate)
        {
            if (preview == null) return;
            float h = (float)preview.Layout.Height;
            float fov = cameraController != null && cameraController.Camera != null ? cameraController.Camera.fieldOfView : 60f;
            targetFocus = 0.48f * h;
            targetDistance = Mathf.Clamp(0.5f * h / Mathf.Tan(0.5f * fov * Mathf.Deg2Rad) * 1.3f, 8f, MaxDistance);
            targetPitch = 4f;
            targetYaw = Mathf.Atan2(0.5f * HangarBuilding.InteriorWidth, 0.5f * HangarBuilding.InteriorDepth) * Mathf.Rad2Deg;
            if (immediate) Snap();
        }

        /// <summary>Turns to the first part matching <paramref name="match"/> and, if asked, makes it blink (a new module).</summary>
        public bool FocusOn(Predicate<PartPlacement> match, bool flash)
        {
            if (preview == null || match == null) return false;
            foreach (RocketModel.Part p in preview.Parts)
            {
                if (!match(p.Placement)) continue;
                Focus(p.Placement);
                if (flash)
                {
                    flashed = p;
                    flashUntil = Time.unscaledTime + 1.8f;
                }
                return true;
            }
            return false;
        }

        void Focus(PartPlacement p)
        {
            targetFocus = (float)p.Center;
            float size = Mathf.Max((float)p.Height, (float)p.Diameter);
            if (p.Role == PartRole.Engine) size = Mathf.Max(size, (float)preview.Layout.Radius * 2f); // the whole cluster
            targetDistance = Mathf.Clamp(size * 3.2f + 4f, 6f, MaxDistance);
            if (p.Role == PartRole.Instrument) targetYaw = (float)p.AngleDegrees; // face the instrument
        }

        /// <summary>Sets the view directly (bridge, tests): azimuth, elevation [deg], distance [m], aim height above the stand [m].</summary>
        public void SetView(float yawDegrees, float pitchDegrees, float distanceMetres, float focusHeight)
        {
            targetYaw = yawDegrees;
            targetPitch = Mathf.Clamp(pitchDegrees, -30f, 85f);
            targetDistance = Mathf.Clamp(distanceMetres, MinDistance, MaxDistance);
            targetFocus = focusHeight;
            Snap();
        }

        void Snap()
        {
            yaw = targetYaw;
            pitch = targetPitch;
            distance = targetDistance;
            focus = targetFocus;
        }

        void Update()
        {
            if (!inside || preview == null) return;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 1e-4f, 0.1f);
            if (!FlightInput.PauseMenuOpen) HandleInput(dt);
            float kr = 1f - Mathf.Exp(-14f * dt), kz = 1f - Mathf.Exp(-8f * dt);
            yaw = Mathf.LerpAngle(yaw, targetYaw, kr);
            pitch = Mathf.Lerp(pitch, targetPitch, kr);
            distance = Mathf.Exp(Mathf.Lerp(Mathf.Log(distance), Mathf.Log(targetDistance), kz));
            focus = Mathf.Lerp(focus, targetFocus, kz);
            if (!FlightInput.PauseMenuOpen) UpdatePicking();
            UpdateTints();
        }

        void HandleInput(float dt)
        {
            if (!PointerOverUi)
            {
                if (FlightInput.RightMouseHeld)
                {
                    Vector2 d = FlightInput.LookDelta;
                    targetYaw += d.x * OrbitDegreesPerPixel;
                    targetPitch = Mathf.Clamp(targetPitch - d.y * OrbitDegreesPerPixel, -30f, 85f);
                }
                if (FlightInput.MiddleMouseHeld) targetFocus -= FlightInput.MouseDelta.y * distance * 0.0022f;
                float notches = FlightInput.ScrollNotches;
                if (Mathf.Abs(notches) > 0f) targetDistance = Mathf.Clamp(targetDistance * Mathf.Exp(-notches * 0.15f), MinDistance, MaxDistance);
            }
            if (GUIUtility.keyboardControl != 0) return;
            float climb = Mathf.Max(4f, distance * 0.6f) * dt;
            if (FlightInput.GetKey(InputKey.W)) targetFocus += climb;
            if (FlightInput.GetKey(InputKey.S)) targetFocus -= climb;
            if (FlightInput.GetKey(InputKey.A)) targetYaw += 70f * dt;
            if (FlightInput.GetKey(InputKey.D)) targetYaw -= 70f * dt;
            if (FlightInput.GetKey(InputKey.Equals)) targetDistance = Mathf.Clamp(targetDistance * Mathf.Exp(-2f * dt), MinDistance, MaxDistance);
            if (FlightInput.GetKey(InputKey.Minus)) targetDistance = Mathf.Clamp(targetDistance * Mathf.Exp(2f * dt), MinDistance, MaxDistance);
            if (FlightInput.GetKeyDown(InputKey.F)) FrameAll(false);
            targetFocus = Mathf.Clamp(targetFocus, 0f, (float)preview.Layout.Height + 2f);
        }

        Vector3 FocusLocal => HangarBuilding.BayLocal + new Vector3(0f, focus, 0f);

        /// <summary>Camera position in the building's frame: orbiting the aim point, kept inside the hall and under the crane.</summary>
        public Vector3 EyeLocal
        {
            get
            {
                float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p)) * distance;
                Vector3 eye = HangarBuilding.ClampInside(FocusLocal + offset, EyeMargin);
                eye.y = Mathf.Min(eye.y, HangarBuilding.FloorHeight + HangarBuilding.InteriorHeight - CeilingClearance);
                return eye;
            }
        }

        /// <summary><see cref="SpaceCameraController"/> looks through this view while the rocket is in the hall.</summary>
        public void GetView(double renderTime, out Vector3d eye, out Vector3d forward, out Vector3d up)
        {
            Vector3 eyeLocal = EyeLocal;
            Vector3 look = FocusLocal - eyeLocal;
            if (look.sqrMagnitude < 1e-6f) look = new Vector3(0f, 0f, -1f);
            eye = building.LocalToSimulation(eyeLocal, renderTime);
            forward = building.DirectionToSimulation(look.normalized, renderTime);
            up = building.DirectionToSimulation(Vector3.up, renderTime);
        }

        // ---- Picking and highlighting -----------------------------------------------------------------------

        void UpdatePicking()
        {
            RocketModel.Part hit = null;
            Camera cam = cameraController != null ? cameraController.Camera : null;
            if (!PointerOverUi && cam != null && !FlightInput.RightMouseHeld && !FlightInput.MiddleMouseHeld)
                hit = Pick(cam.ScreenPointToRay(FlightInput.MousePosition));
            hovered = hit;
            if (!PointerOverUi && FlightInput.LeftMouseDown)
            {
                selected = hit;
                if (hit != null) PartClicked?.Invoke(hit);
            }
        }

        /// <summary>Nearest module slot under the ray (slot boxes in each slot's own frame); the fairing, when shown, hides the payload.</summary>
        public RocketModel.Part Pick(Ray ray)
        {
            if (preview == null) return null;
            float best = float.MaxValue;
            RocketModel.Part bestPart = null;
            foreach (RocketModel.Part part in preview.Parts)
            {
                PartPlacement p = part.Placement;
                bool fairing = p.Role == PartRole.Fairing;
                if (fairing ? !showFairing : !p.IsModule) continue;
                if (part.Transform == null || !part.Transform.gameObject.activeInHierarchy) continue;
                Vector3 o = part.Transform.InverseTransformPoint(ray.origin);
                Vector3 d = part.Transform.InverseTransformDirection(ray.direction);
                float half = 0.5f * (float)p.Diameter;
                if (RayBox(o, d, new Vector3(-half, 0f, -half), new Vector3(half, (float)p.Height, half), out float t) && t < best)
                {
                    best = t;
                    bestPart = part;
                }
            }
            return bestPart != null && bestPart.Placement.Role == PartRole.Fairing ? null : bestPart;
        }

        static bool RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t)
        {
            float near = 0f, far = float.MaxValue;
            t = 0f;
            for (int a = 0; a < 3; a++)
            {
                float oa = a == 0 ? o.x : a == 1 ? o.y : o.z;
                float da = a == 0 ? d.x : a == 1 ? d.y : d.z;
                float lo = a == 0 ? min.x : a == 1 ? min.y : min.z;
                float hi = a == 0 ? max.x : a == 1 ? max.y : max.z;
                if (Mathf.Abs(da) < 1e-8f)
                {
                    if (oa < lo || oa > hi) return false;
                    continue;
                }
                float t1 = (lo - oa) / da, t2 = (hi - oa) / da;
                if (t1 > t2)
                {
                    float s = t1;
                    t1 = t2;
                    t2 = s;
                }
                near = Mathf.Max(near, t1);
                far = Mathf.Min(far, t2);
                if (near > far) return false;
            }
            t = near;
            return true;
        }

        /// <summary>Hovered part lightly tinted, the selected one stronger, a newly installed one blinks for a moment.</summary>
        void UpdateTints()
        {
            var wanted = new Dictionary<RocketModel.Part, float>();
            void Want(RocketModel.Part p, float amount)
            {
                if (p == null || amount <= 0f) return;
                wanted[p] = wanted.TryGetValue(p, out float a) ? Mathf.Max(a, amount) : amount;
            }
            if (HighlightStage.HasValue && preview != null)
                foreach (RocketModel.Part p in preview.Parts)
                    if (p.Placement.Stage == HighlightStage.Value && p.Placement.IsModule) Want(p, 0.22f);
            Want(hovered, 0.28f);
            Want(selected, 0.5f);
            if (flashed != null && Time.unscaledTime < flashUntil)
                Want(flashed, 0.15f + 0.5f * (0.5f + 0.5f * Mathf.Sin((flashUntil - Time.unscaledTime) * 12f)));
            else flashed = null;

            var stale = new List<RocketModel.Part>();
            foreach (KeyValuePair<RocketModel.Part, float> kv in tinted)
                if (!wanted.ContainsKey(kv.Key)) stale.Add(kv.Key);
            foreach (RocketModel.Part p in stale)
            {
                RocketModel.Tint(p, HighlightColor, 0f);
                tinted.Remove(p);
            }
            foreach (KeyValuePair<RocketModel.Part, float> kv in wanted)
            {
                if (tinted.TryGetValue(kv.Key, out float a) && Mathf.Abs(a - kv.Value) < 0.01f) continue;
                RocketModel.Tint(kv.Key, HighlightColor, kv.Value);
                tinted[kv.Key] = kv.Value;
            }
        }
    }
}
