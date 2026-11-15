using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The hangar (Milestone 2, now a real hall): "АНГАР" takes the rocket from the pad into the assembly hangar
    /// (<see cref="HangarFacility"/>), where the player installs modules and sees them on the rocket in 3D. The menu around the
    /// view edits a copy of the current design; every change is recompiled by <see cref="BlueprintCompiler"/> and analysed by
    /// <see cref="RocketDesignAnalysis"/> (Level A, ideal), and the preview is rebuilt from the same design. This class only formats
    /// those results and adds simple sums — no physics formulas here. Unusual designs are never refused: construction issues and
    /// design warnings explain the consequences instead. "Вывезти на старт" applies the design (a new rocket on the pad).
    /// </summary>
    /// <remarks>
    /// The class is split by topic (Scripts/Runtime/UI/Hangar): HangarUI.Edits (changes to the design, named projects),
    /// HangarUI.Layout (frame of the screen, top bar, summary, dialogs), HangarUI.Catalog (category rail, search, sorting, the
    /// part grid, drag and drop), HangarUI.Card (part card on hover), HangarUI.Stages (stage stack), HangarUI.Details (collapsible
    /// project / statistics / warnings panel), HangarUI.Format (IMGUI helpers and number formatting).
    /// IMGUI rule followed here: anything that changes the number of controls (adding/removing stages, tanks, modules,
    /// switching tabs) is queued with <see cref="Defer"/> and applied after the whole pass, so the Layout pass and the
    /// following event pass always see the same structure. Recompilation also happens only between passes.
    /// </remarks>
    [DefaultExecutionOrder(215)]
    public sealed partial class HangarUI : MonoBehaviour
    {
        /// <summary>Seconds to fade to black (and a little longer back) when the rocket moves between the pad and the hall.</summary>
        const float FadeTime = 0.35f;
        /// <summary>Tank slider snaps to this fraction of the capacity (0.5 %).</summary>
        const double SliderStep = 0.005;

        static readonly ModuleCategory[] Categories = ModuleCatalog.Categories;

        FlightBody body;
        FlightStateController state;
        HangarFacility facility;
        ShopUI shop;
        PlayerSession session;
        bool open;

        RocketBlueprint design;
        CompiledRocket compiled;
        RocketDesignReport report;
        bool dirty;
        bool modified;
        /// <summary>Grows with every recompilation: cached previews ("what if this part is installed") are keyed by it.</summary>
        int editVersion;
        double lastGravity, lastRequired;

        MissionTarget[] targets;
        object targetsBuiltFor;
        /// <summary>Index into <see cref="targets"/> used as the required Δv. StandardTargets order: hop, orbit, escape.</summary>
        int requiredTarget = 1;

        int selectedStage;
        string status = "";
        Color statusColor = Color.white;
        /// <summary>After the next rebuild the camera turns to this part and it blinks (the module just installed).</summary>
        Predicate<PartPlacement> focusRequest;

        // Fade between the pad and the hall: +1 darkening (then <see cref="atBlack"/> runs), −1 brightening.
        float fade;
        int fadeDirection;
        Action atBlack;
        bool confirmExit;
        bool shopWasOpen;

        readonly List<Action> pending = new List<Action>();

        /// <summary>The rocket is in the hangar (or on its way in or out).</summary>
        public bool IsOpen => open;
        /// <summary>Fading between the pad and the hall.</summary>
        public bool Busy => fadeDirection != 0;
        /// <summary>The design being edited (a copy; applied only by <see cref="Leave"/> with apply).</summary>
        public RocketBlueprint Design => design;
        public HangarFacility Facility => facility;
        public bool ConfirmingExit => confirmExit;
        public bool Modified => modified;

        public void Initialize(FlightBody flightBody, FlightStateController flightState, HangarFacility hangarFacility = null)
        {
            body = flightBody;
            state = flightState;
            facility = hangarFacility;
            if (facility != null) facility.PartClicked += OnPartClicked;
        }

        /// <summary>The shop can be opened from the hangar (buy a module, install it right away).</summary>
        public void SetShop(ShopUI shopUi) => shop = shopUi;

        /// <summary>The player's profile: the budget shown under the catalog and the folder of the named projects.</summary>
        public void SetSession(PlayerSession playerSession) => session = playerSession;

        void OnDestroy()
        {
            if (facility != null) facility.PartClicked -= OnPartClicked;
        }

        /// <summary>
        /// B / the HUD button / Esc. Outside: takes the rocket into the hangar. Inside: closes an open dialog first, then leaves —
        /// straight away when nothing changed, otherwise it first asks whether to apply (a second press keeps the player in the hangar).
        /// </summary>
        public void Toggle()
        {
            if (Busy) return;
            if (!open)
            {
                Enter();
                return;
            }
            if (projectsOpen)
            {
                projectsOpen = false;
                return;
            }
            if (confirmExit)
            {
                confirmExit = false;
                return;
            }
            if (modified)
            {
                confirmExit = true;
                return;
            }
            Leave(false);
        }

        /// <summary>The rocket goes from the pad into the hall with a fresh copy of the current design.</summary>
        public void Enter(bool instant = false)
        {
            if (open || Busy || body == null) return;
            if (facility != null && !facility.CanEnter(out string reason))
            {
                state?.Notify(Loc.T("Ангар недоступен: ") + reason + ".");
                return;
            }
            design = (body.Blueprint ?? RocketBlueprint.CreateDefault()).Clone();
            design.EnsureLists();
            compiled = null;
            report = null;
            selectedStage = 0;
            modified = false;
            confirmExit = false;
            projectsOpen = false;
            status = "";
            tab = Tab.Design;
            pending.Clear();
            focusRequest = null;
            CancelDrag();
            open = true;
            dirty = true;
            StartFade(() =>
            {
                Refresh();
                facility?.Enter(design);
            }, instant);
        }

        /// <summary>Back to the pad. With <paramref name="apply"/> the edited design becomes the new rocket on the pad; otherwise it is dropped.</summary>
        public void Leave(bool apply, bool instant = false)
        {
            if (!open || Busy) return;
            confirmExit = false;
            projectsOpen = false;
            if (apply)
            {
                Refresh();
                if (!body.IsOnLaunchPad)
                {
                    SetStatus(Loc.T("Перестройка возможна только на стартовой площадке."), HudStyle.Warning);
                    return;
                }
                if (!CheckOwnedForLaunch()) return; // Career: every part must be bought (HangarUI.Mode.cs)
            }
            CancelDrag();
            StartFade(() =>
            {
                if (apply) ApplyDesign();
                facility?.Exit();
                open = false;
                pending.Clear();
                GUIUtility.keyboardControl = 0;
            }, instant);
        }

        void StartFade(Action action, bool instant)
        {
            if (instant)
            {
                fade = 0f;
                fadeDirection = 0;
                action?.Invoke();
                return;
            }
            atBlack = action;
            fadeDirection = 1;
        }

        void Update()
        {
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            if (fadeDirection > 0)
            {
                fade += dt / FadeTime;
                if (fade >= 1f)
                {
                    fade = 1f;
                    fadeDirection = -1;
                    Action a = atBlack;
                    atBlack = null;
                    a?.Invoke();
                }
            }
            else if (fadeDirection < 0)
            {
                fade -= dt / (FadeTime * 1.5f);
                if (fade <= 0f)
                {
                    fade = 0f;
                    fadeDirection = 0;
                }
            }
            // A purchase in the shop changes what may be installed: recompile and rebuild the preview when it closes.
            bool shopOpen = shop != null && shop.IsOpen;
            if (shopWasOpen && !shopOpen && open) dirty = true;
            shopWasOpen = shopOpen;
        }

        // ---- Model ---------------------------------------------------------------------------

        /// <summary>Recompiles the edited design (if it changed), re-runs the Level A analysis and rebuilds the 3D preview.</summary>
        void Refresh()
        {
            if (body == null || design == null) return;
            PlanetRuntime planet = body.Planet;
            if (planet == null || planet.Model == null) return;
            var model = planet.Model;
            if (targets == null || !ReferenceEquals(targetsBuiltFor, model))
            {
                targets = BuildTargets(planet);
                targetsBuiltFor = model;
                dirty = true;
            }
            if (!dirty && compiled != null && report != null) return;
            dirty = false;

            if (requiredTarget >= targets.Length) requiredTarget = targets.Length - 1;
            if (requiredTarget < 0) requiredTarget = 0;
            lastRequired = targets.Length > 0 ? targets[requiredTarget].IdealDeltaV : 0.0;
            lastGravity = model.Properties.SurfaceGravity;

            compiled = body.CompileDesign(design);
            report = RocketDesignAnalysis.Analyze(compiled.Configuration, lastGravity, lastRequired);
            editVersion++;

            if (selectedStage >= design.stages.Count) selectedStage = design.stages.Count - 1;
            if (selectedStage < 0) selectedStage = 0;

            if (facility != null && facility.Inside)
            {
                facility.ShowDesign(design);
                if (focusRequest != null) facility.FocusOn(focusRequest, true);
                focusRequest = null;
            }
        }

        static MissionTarget[] BuildTargets(PlanetRuntime planet)
        {
            var model = planet.Model;
            // Ground speed at the pad comes from the planet model (ω × r), not from a formula in the UI.
            Vector3d pad = planet.FixedToSimulation(planet.LaunchSiteDirection() * model.Radius, 0.0);
            double groundSpeed = model.SurfaceVelocityAt(pad).Magnitude;
            bool hasAtmosphere = model.Atmosphere != null && model.Atmosphere.ReferenceDensity > 0.0;
            return MissionRequirements.StandardTargets(model.GravitationalParameter, model.Radius, groundSpeed,
                model.SpaceBoundaryAltitude, hasAtmosphere);
        }

        /// <summary>Shop modules stay locked until the player owns them (profile ownership via FlightBody).</summary>
        bool IsLocked(ModuleDefinition m) => body != null && !body.OwnsModule(m.id);

        /// <summary>A part was clicked on the rocket: its stage becomes the current one, its category opens in the catalog.</summary>
        void OnPartClicked(RocketModel.Part part)
        {
            if (!open || part == null) return;
            PartPlacement p = part.Placement;
            if (p.Stage >= 0 && design != null && p.Stage < design.stages.Count) selectedStage = p.Stage;
            ModuleDefinition m = ModuleCatalog.Find(p.ModuleId);
            if (m != null)
            {
                if (category != m.category) SelectCategory(m.category);
                if (subgroup != ModuleSubgroup.None && m.Subgroup != subgroup) subgroup = ModuleSubgroup.None;
                selectedModule = m.id;
            }
            tab = Tab.Design;
        }

        // ---- Deferred changes (always applied between IMGUI passes) ----------------------------

        void Defer(Action action)
        {
            if (action != null) pending.Add(action);
        }

        void ApplyPending()
        {
            if (pending.Count == 0) return;
            Action[] actions = pending.ToArray();
            pending.Clear();
            foreach (Action a in actions) a();
            if (open) Refresh();
        }

        void MarkChanged()
        {
            dirty = true;
            modified = true;
            status = "";
        }

        void SetStatus(string text, Color color)
        {
            status = text;
            statusColor = color;
        }
    }
}
