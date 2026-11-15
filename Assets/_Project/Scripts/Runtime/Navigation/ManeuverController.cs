using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpaceSim.Flight;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Navigation;
using SpaceSim.Physics;
using SpaceSim.Multiplayer;
using SpaceSim.Runtime.Multiplayer;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>An SOI encounter of the node's preview: which body, when, and the closest approach on that patch.</summary>
    public struct EncounterInfo
    {
        public int Body;
        public double Time;
        /// <summary>Periapsis distance from the body's centre [m] and when it is reached.</summary>
        public double Periapsis, PeriapsisTime;
        public bool Impact;
    }

    /// <summary>Everything the panels show about the planned node (recomputed when the prediction changes).</summary>
    public sealed class NodePreview
    {
        public bool Valid;
        public ManeuverNode Node;
        /// <summary>State just before the burn (relative to <see cref="CoastState.Primary"/>).</summary>
        public CoastState PreBurn;
        public OrbitPreview Before, After;
        public double BodyRadius;
        public BurnEstimate Burn;
        public readonly List<EncounterInfo> Encounters = new List<EncounterInfo>();
        /// <summary>Closest approach to the selected target over the preview (centre distance) [m] and time; NaN without a target.</summary>
        public double TargetClosest = double.NaN, TargetClosestTime;
    }

    /// <summary>Where the warp is heading.</summary>
    public sealed class WarpTarget
    {
        public double Time;
        /// <summary>The point on the trajectory (relative to its primary at <see cref="Time"/>), if the warp aims at a point.</summary>
        public bool HasPoint;
        public CoastState Point;
        public bool ToNode;
        public string Label = "";
    }

    public enum BurnMode
    {
        None,
        /// <summary>The flight computer turns the rocket to the node and runs the engine through the propulsion model.</summary>
        Assisted,
        /// <summary>Sandbox/cheat: the Δv is added to the velocity at the node time without burning propellant.</summary>
        InstantSandbox
    }

    /// <summary>
    /// Flight planning actions of the map and HUD: a selected point on the trajectory, warp to that point or to the node (ramping down
    /// and stopping there, within the <see cref="TimeWarpPolicy"/> limit), manoeuvre-node editing with a live patched-conic preview,
    /// burn execution (assisted through the real propulsion model, or flown by the player with the guidance readout) and
    /// interplanetary transfer planning (<see cref="TransferPlanner"/>). All numbers come from Core.
    /// </summary>
    [DefaultExecutionOrder(165)]
    public sealed class ManeuverController : MonoBehaviour
    {
        /// <summary>Turn rates of the flight computer [deg per real second] — the base rates of the manual controls.</summary>
        const double PitchRate = 12.0, HeadingRate = 20.0;
        const double NodeMargin = NavigationService.WarpToBurnLead;

        SpaceSimBootstrap boot;
        NavigationService nav;

        public TrajectoryPoint Selected { get; private set; }
        public bool HasSelection => Selected.Valid;

        public WarpTarget Warp { get; private set; }
        public bool Warping => Warp != null;
        public bool WarpIsToNode => Warp != null && Warp.ToNode;

        /// <summary>The node is locked (handles hidden) and may be executed.</summary>
        public bool NodeConfirmed { get; private set; }
        public BurnMode Mode { get; private set; }
        public string BurnStatus { get; private set; } = "";
        Vector3d burnDirection;
        bool ignitedByComputer;
        double deltaVAtStart;

        public NodePreview Preview { get; } = new NodePreview();
        TrajectoryPrediction previewFrom;
        ManeuverNode previewNode;

        public TransferPlan Transfer { get; private set; }
        public bool TransferBusy => transferTask != null && !transferTask.IsCompleted;
        Task<(TransferPlan plan, ManeuverNode refined)> transferTask;
        bool transferMakeNode;
        public string TransferStatus { get; private set; } = "";

        public ManeuverNode Node => nav != null ? nav.Plan.Departure : null;

        public void Initialize(SpaceSimBootstrap bootstrap, NavigationService navigation)
        {
            boot = bootstrap;
            nav = navigation;
        }

        FreeFlightSimulation Sim => boot != null && boot.Body != null ? boot.Body.Simulation : null;
        double Now => boot.Simulation.SimulationTime;

        /// <summary>Team mission: planning and burns belong to the pilot (the others still see the plan and may select points).</summary>
        bool PilotAllowed() => CrewGate.Allows(CrewAction.PlanFlight, boot != null ? boot.FlightState : null);

        void Notify(string text)
        {
            if (boot.FlightState != null) boot.FlightState.Notify(text);
        }

        // ---- Selection and warp to a point ---------------------------------------------------------------------------------

        public void Select(TrajectoryPoint point) => Selected = point;
        public void ClearSelection() => Selected = default;

        /// <summary>Warp to the selected point of the trajectory.</summary>
        public void WarpToSelected()
        {
            if (!HasSelection || !PilotAllowed()) return;
            if (Selected.Time <= Now + WarpApproach.ArrivalTolerance)
            {
                Notify(Loc.T("Эта точка уже пройдена."));
                return;
            }
            StartWarp(new WarpTarget { Time = Selected.Time, HasPoint = true, Point = Selected.State, Label = Loc.T("точка орбиты") });
        }

        /// <summary>Warp to the node: stops half the burn plus a margin before it (the burn is centred on the node).</summary>
        public void WarpToNode()
        {
            ManeuverNode node = Node;
            if (node == null || !PilotAllowed()) return;
            double lead = WarpApproach.NodeLead(nav.Guidance.BurnTime, NodeMargin);
            double t = node.Time - lead;
            if (t <= Now + 1.0)
            {
                Notify(Loc.T("До манёвра меньше, чем нужно на разворот и половину прожига — ускорять время некуда."));
                return;
            }
            var w = new WarpTarget { Time = t, ToNode = true, Label = Loc.T("манёвр") };
            if (TrajectoryPoints.StateAt(nav.System, nav.BasePrediction, node.Time, out CoastState s, out _))
            {
                w.HasPoint = true;
                w.Point = s;
            }
            StartWarp(w);
        }

        void StartWarp(WarpTarget target)
        {
            if (Sim == null || Sim.IsLanded)
            {
                Notify(Loc.T("Ускорение до точки — только в полёте."));
                return;
            }
            Warp = target;
            boot.Simulation.SetPaused(false);
        }

        public void CancelWarp()
        {
            if (Warp == null) return;
            Warp = null;
            boot.Simulation.ResetWarp();
        }

        public double WarpRemaining => Warp != null ? Warp.Time - Now : 0.0;

        /// <summary>Distance from the vehicle now to the target point (which moves with its primary) [m]; NaN without a point.</summary>
        public double DistanceToWarpPoint()
        {
            if (Warp == null || !Warp.HasPoint || nav.System == null) return double.NaN;
            Vector3d p = Warp.Point.Position + nav.System.Position(Warp.Point.Primary, Now);
            return (p - nav.VehicleStarPosition()).Magnitude;
        }

        /// <summary>Speed relative to the current primary now [m/s].</summary>
        public double CurrentSpeed() => Sim != null && Sim.Navigation != null ? Sim.CoastStateNow().Velocity.Magnitude : 0.0;

        void UpdateWarp()
        {
            if (Warp == null) return;
            FreeFlightSimulation sim = Sim;
            if (sim == null || sim.IsLanded)
            {
                CancelWarp();
                return;
            }
            if (sim.Rocket != null && sim.Rocket.IsBurning && Mode == BurnMode.None)
            {
                // Thrust changes the trajectory: the point is no longer where the vehicle is going.
                CancelWarp();
                Notify(Loc.T("Ускорение до точки остановлено: работает двигатель."));
                return;
            }
            if (boot.Simulation.Paused) return;
            double remaining = Warp.Time - Now;
            double warp = WarpApproach.Command(remaining, boot.Simulation.WarpLimit, boot.Simulation.Settings.maxRealDeltaTime, out bool arrived);
            if (arrived)
            {
                string label = Warp.Label;
                Warp = null;
                boot.Simulation.ResetWarp();
                Notify(Loc.T("Прибыли: ") + label + ".");
                return;
            }
            boot.Simulation.SetWarp(warp);
        }

        // ---- Node editing --------------------------------------------------------------------------------------------------

        /// <summary>Creates a node (zero Δv) at the selected point.</summary>
        public void CreateNodeAtSelected()
        {
            if (!HasSelection) return;
            if (Selected.Time <= Now + 1.0)
            {
                Notify(Loc.T("Узел манёвра ставится в будущей точке траектории."));
                return;
            }
            CreateNode(new ManeuverNode { Time = Selected.Time });
            ClearSelection();
        }

        public void CreateNode(ManeuverNode node)
        {
            if (!PilotAllowed()) return;
            AbortBurn(false);
            if (Warp != null && Warp.ToNode) CancelWarp();
            NodeConfirmed = false;
            nav.SetNode(node);
        }

        public void SetComponents(double prograde, double normal, double radial)
        {
            ManeuverNode n = Node;
            if (n == null || NodeConfirmed || !PilotAllowed()) return;
            n = n.Clone();
            n.Prograde = prograde;
            n.Normal = normal;
            n.Radial = radial;
            nav.UpdateNodeLive(n);
        }

        /// <summary>Adds to one component: 0 = prograde (X), 1 = normal (Y), 2 = radial (Z).</summary>
        public void AddComponent(int axis, double delta)
        {
            ManeuverNode n = Node;
            if (n == null) return;
            SetComponents(n.Prograde + (axis == 0 ? delta : 0.0), n.Normal + (axis == 1 ? delta : 0.0), n.Radial + (axis == 2 ? delta : 0.0));
        }

        /// <summary>Moves the node along the trajectory by <paramref name="seconds"/> (never into the past).</summary>
        public void ShiftNode(double seconds)
        {
            ManeuverNode n = Node;
            if (n == null || NodeConfirmed || !PilotAllowed()) return;
            n = n.Clone();
            n.Time = Math.Max(Now + 1.0, n.Time + seconds);
            nav.UpdateNodeLive(n);
        }

        public void DeleteNode()
        {
            if (!PilotAllowed()) return;
            AbortBurn(false);
            if (Warp != null && Warp.ToNode) CancelWarp();
            NodeConfirmed = false;
            nav.ClearNode();
        }

        public void ConfirmNode(bool confirmed)
        {
            if (Node == null || !PilotAllowed()) return;
            NodeConfirmed = confirmed;
            if (confirmed) nav.RefreshNow();
        }

        // ---- Preview ----------------------------------------------------------------------------------------------------

        void UpdatePreview()
        {
            ManeuverNode node = Node;
            TrajectoryPrediction p = nav.Prediction;
            if (node == null || nav.System == null)
            {
                Preview.Valid = false;
                previewFrom = null;
                return;
            }
            bool sameNode = previewNode != null && previewNode.Time == node.Time && previewNode.Prograde == node.Prograde &&
                            previewNode.Normal == node.Normal && previewNode.Radial == node.Radial;
            if (ReferenceEquals(p, previewFrom) && sameNode && Preview.Valid)
            {
                Preview.Burn = ManeuverPlanning.Estimate(boot.Body != null ? boot.Body.Rocket : null, node.Magnitude);
                return;
            }
            previewFrom = p;
            previewNode = node.Clone();
            PatchedConicSystem sys = nav.System;
            Preview.Node = node;
            Preview.Encounters.Clear();
            Preview.TargetClosest = double.NaN;
            Preview.Burn = ManeuverPlanning.Estimate(boot.Body != null ? boot.Body.Rocket : null, node.Magnitude);

            if (!PreBurnState(sys, node.Time, out CoastState pre))
            {
                Preview.Valid = false;
                return;
            }
            Preview.PreBurn = pre;
            Vector3d axis = ReferenceAxis(pre.Primary);
            double mu = sys.Mu(pre.Primary);
            Preview.Before = ManeuverPlanning.Describe(mu, pre.Primary, pre.Position, pre.Velocity, axis);
            Preview.After = ManeuverPlanning.AfterBurn(mu, pre.Primary, pre.Position, pre.Velocity, node, axis);
            Preview.BodyRadius = pre.Primary < 0 ? sys.StarProperties.radius : sys[pre.Primary].Radius;
            Preview.Valid = true;

            if (p == null || !p.NodeApplied) return;
            for (int i = 1; i < p.Patches.Count; i++)
            {
                PredictedPatch patch = p.Patches[i];
                PatchTransition tr = default;
                bool entry = false;
                foreach (PatchTransition t in p.Transitions)
                    if (Math.Abs(t.Time - patch.StartTime) < 1e-6 && t.Kind == PatchEvent.SoiEntry && t.To == patch.Primary)
                    {
                        tr = t;
                        entry = true;
                    }
                if (!entry || patch.Primary < 0) continue;
                var info = new EncounterInfo { Body = patch.Primary, Time = tr.Time };
                OrbitApsides aps = OrbitApsides.FromState(sys.Mu(patch.Primary), patch.Start.Position, patch.Start.Velocity);
                info.Periapsis = aps.PeriapsisRadius;
                double tp = aps.HasLine ? aps.TimeToPeriapsis : 0.0;
                info.PeriapsisTime = patch.StartTime + (double.IsNaN(tp) || double.IsInfinity(tp) ? 0.0 : tp);
                info.Impact = aps.PeriapsisRadius < sys[patch.Primary].Radius;
                Preview.Encounters.Add(info);
            }
            if (p.Target >= 0 && !double.IsInfinity(p.ClosestDistance))
            {
                Preview.TargetClosest = p.Encounter ? p.ArrivalPeriapsis : p.ClosestDistance;
                Preview.TargetClosestTime = p.Encounter ? p.EncounterTime : p.ClosestTime;
            }
        }

        /// <summary>State just before the node: from the prediction without the node, or by coasting the current state.</summary>
        public bool PreBurnState(PatchedConicSystem sys, double time, out CoastState state)
        {
            if (TrajectoryPoints.StateAt(sys, nav.BasePrediction, time, out state, out _)) return true;
            FreeFlightSimulation sim = Sim;
            if (sim == null || sim.Navigation == null || sim.IsLanded) return false;
            state = sim.CoastStateNow();
            if (time < state.Time) return false;
            for (int guard = 0; guard < 10000 && state.Time < time; guard++)
                sys.Coast(ref state, Math.Min(time - state.Time, Math.Max(1e-3, sys.Horizon(state, time - state.Time, true))));
            return true;
        }

        /// <summary>Inclination reference: the spin axis of the body the simulation frame is centred on, the system's plane otherwise.</summary>
        public Vector3d ReferenceAxis(int primary)
        {
            FreeFlightSimulation sim = Sim;
            if (sim != null && primary >= 0 && primary == sim.FrameBody && boot.Body.Planet != null) return boot.Body.Planet.Model.Rotation.SpinAxis;
            return Vector3d.UnitZ;
        }

        // ---- Execution -----------------------------------------------------------------------------------------------------

        /// <summary>Assisted burn: the flight computer turns to the node and runs the real engine until the remaining Δv ≈ 0.</summary>
        public void StartAssistedBurn()
        {
            RocketState r = boot.Body != null ? boot.Body.Rocket : null;
            if (Node == null || !PilotAllowed()) return;
            if (r == null || !r.HasStages || r.DeltaVRemaining() <= 0.0)
            {
                Notify(Loc.T("Нет двигателя или топлива: манёвр выполнить нечем."));
                return;
            }
            if (!r.CanThrottle)
            {
                Notify(Loc.T("Твердотопливная ступень не дросселируется и не выключается: точный прожиг невозможен. Отделите её или летите вручную."));
                return;
            }
            if (boot.Body.Simulation.IsLanded)
            {
                Notify(Loc.T("Манёвр выполняется в полёте."));
                return;
            }
            NodeConfirmed = true;
            Mode = BurnMode.Assisted;
            ignitedByComputer = false;
            deltaVAtStart = r.DeltaVRemaining();
            burnDirection = Vector3d.Zero;
            BurnStatus = Loc.T("разворот к узлу");
            Notify(Loc.T("Помощник: разворот к узлу манёвра; двигатель включится за половину прожига до узла."));
        }

        /// <summary>
        /// The instant (propellant-free) manoeuvre is a Sandbox tool: hidden and refused in a Career, where Δv must be paid for.
        /// (In Sandbox it still marks the flight as test-tool use, like the initial-conditions window.)
        /// </summary>
        public bool InstantBurnAllowed => boot == null || boot.Session == null || boot.Session.Profile == null || !boot.Session.Profile.IsCareer;

        /// <summary>Sandbox/cheat: adds the node's Δv to the velocity at the node time — no propellant is burnt; flight rewards stop counting.</summary>
        public void StartInstantSandbox()
        {
            if (Node == null || !PilotAllowed()) return;
            if (!InstantBurnAllowed)
            {
                Notify(Loc.T("Карьера: мгновенный манёвр недоступен — выполните его двигателем."));
                return;
            }
            NodeConfirmed = true;
            Mode = BurnMode.InstantSandbox;
            BurnStatus = Loc.T("мгновенно в момент узла (песочница)");
            if (Node.Time - Now > 1.0) StartWarp(new WarpTarget { Time = Node.Time, ToNode = true, Label = Loc.T("манёвр") });
        }

        public void AbortBurn(bool notify = true)
        {
            if (Mode == BurnMode.None) return;
            RocketState r = boot.Body != null ? boot.Body.Rocket : null;
            if (Mode == BurnMode.Assisted && r != null && ignitedByComputer && r.CanThrottle)
            {
                r.Throttle = 0.0;
                r.EngineOn = false;
            }
            Mode = BurnMode.None;
            BurnStatus = "";
            if (notify) Notify(Loc.T("Выполнение манёвра отменено."));
        }

        void UpdateBurn()
        {
            if (Mode == BurnMode.None) return;
            ManeuverNode node = Node;
            FlightBody body = boot.Body;
            FreeFlightSimulation sim = Sim;
            if (node == null || body == null || sim == null || body.Destroyed || sim.IsLanded)
            {
                AbortBurn(false);
                return;
            }
            if (Mode == BurnMode.InstantSandbox)
            {
                if (Now + 1e-3 < node.Time) return;
                CoastState s = sim.CoastStateNow();
                Vector3d dv = node.DeltaVector(s.Position, s.Velocity);
                body.ApplyTestImpulse(dv); // marked as a test tool: research rewards stop counting for this flight
                Notify(Loc.T("Песочница: импульс ") + SpaceSim.Units.UnitFormatter.Number(dv.Magnitude, 1) + Loc.T(" м/с добавлен мгновенно, без топлива."));
                Mode = BurnMode.None;
                BurnStatus = "";
                NodeConfirmed = false;
                nav.ClearNode();
                return;
            }

            RocketState r = body.Rocket;
            NodeGuidance g = nav.Guidance;
            if (!g.Active)
            {
                FinishBurn(Loc.T("окно манёвра прошло"));
                return;
            }
            if (r == null || !r.HasStages)
            {
                FinishBurn(Loc.T("топливо кончилось"));
                return;
            }

            // Attitude: turn towards the remaining Δv at the base rates (real seconds, like the manual controls).
            double dt = Time.unscaledDeltaTime;
            if (!(FlightInput.GetKey(InputKey.W) || FlightInput.GetKey(InputKey.S) || FlightInput.GetKey(InputKey.A) || FlightInput.GetKey(InputKey.D)))
            {
                r.PitchDegrees = BurnControl.Approach(r.PitchDegrees, g.Pitch, PitchRate * dt, false);
                r.HeadingDegrees = BurnControl.Approach(r.HeadingDegrees, g.Heading, HeadingRate * dt, true);
            }
            double error = BurnControl.AttitudeError(r.PitchDegrees, r.HeadingDegrees, g.Pitch, g.Heading);

            if (!g.Started && g.TimeToIgnition > 0.0)
            {
                BurnStatus = error > BurnControl.AlignTolerance ? Loc.T("разворот к узлу") : Loc.T("ожидание зажигания");
                return;
            }

            if (burnDirection.SqrMagnitude == 0.0) burnDirection = g.RemainingVector;
            if (g.Started && BurnControl.Finished(g.RemainingVector, burnDirection))
            {
                FinishBurn(Loc.T("остаток Δv ") + SpaceSim.Units.UnitFormatter.Number(g.Remaining, 2) + Loc.T(" м/с"));
                return;
            }
            if (!r.CanBurn)
            {
                r.Throttle = 0.0;
                BurnStatus = r.StageIndex + 1 < r.StageCount
                    ? Loc.T("ступень пуста — отделите её (") + FlightInput.KeyLabel(InputKey.Space) + ")"
                    : Loc.T("нет тяги");
                if (r.StageIndex + 1 >= r.StageCount && r.CurrentPropellant <= 0.0) FinishBurn(Loc.T("топливо кончилось"));
                return;
            }
            if (!r.CanThrottle)
            {
                AbortBurn(false);
                Notify(Loc.T("Твердотопливная ступень: помощник отключён, ступень горит до конца."));
                return;
            }
            double accel = RocketEquations.ThrustFromMassFlow(r.FullThrottleMassFlow, r.CurrentStage.vacuumIsp) / Math.Max(1.0, r.Mass);
            double throttle = error > BurnControl.AlignTolerance ? 0.0 : BurnControl.Throttle(g.Remaining, accel);
            if (!r.EngineOn)
            {
                if (!body.Launch())
                {
                    BurnStatus = Loc.T("нет тяги");
                    return;
                }
                ignitedByComputer = true;
            }
            r.Throttle = throttle;
            BurnStatus = throttle > 0.0 ? Loc.T("прожиг") : Loc.T("доворот");
            // One frame must not overshoot the remaining Δv: cap the warp while burning.
            double cap = BurnControl.MaxWarp(g.Remaining, accel * Math.Max(throttle, BurnControl.MinThrottle), boot.Simulation.Settings.maxRealDeltaTime);
            if (boot.Simulation.TimeWarp > cap) boot.Simulation.SetWarp(cap);
        }

        void FinishBurn(string reason)
        {
            RocketState r = boot.Body != null ? boot.Body.Rocket : null;
            if (r != null && r.CanThrottle)
            {
                r.Throttle = 0.0;
                r.EngineOn = false;
            }
            double used = r != null ? Math.Max(0.0, deltaVAtStart - r.DeltaVRemaining()) : 0.0;
            Notify(Loc.T("Манёвр выполнен (") + reason + Loc.T("). Израсходовано Δv ≈ ") + SpaceSim.Units.UnitFormatter.Number(used, 1) + Loc.T(" м/с."));
            Mode = BurnMode.None;
            BurnStatus = "";
            NodeConfirmed = false;
            nav.ClearNode();
        }

        // ---- Transfer planning -------------------------------------------------------------------------------------------

        /// <summary>Plans a transfer to <paramref name="target"/> in the background; <paramref name="createNode"/> also puts the node at the window.</summary>
        public void PlanTransfer(int target, bool createNode)
        {
            if (createNode && !PilotAllowed()) return; // anyone may compute the window; only the pilot puts the node
            PatchedConicSystem sys = nav.System;
            FreeFlightSimulation sim = Sim;
            if (sys == null || sim == null || sim.Navigation == null || sim.IsLanded)
            {
                TransferStatus = Loc.T("Перелёт планируется с орбиты (аппарат на поверхности).");
                return;
            }
            if (TransferBusy) return;
            if (nav.Plan.Target != target) nav.SetTarget(target);
            CoastState state = sim.CoastStateNow();
            double now = Now;
            double capture = nav.Plan.CaptureAltitude;
            bool refine = createNode && nav.Assisted;
            transferMakeNode = createNode;
            TransferStatus = Loc.T("Расчёт перелёта (Ламберт)…");
            transferTask = Task.Run(() =>
            {
                TransferPlan plan = TransferPlanner.Plan(sys, state, target, now, capture);
                ManeuverNode refined = null;
                if (plan.Ok && refine)
                {
                    double rp = sys[target].Radius + capture;
                    refined = TransferPlanner.Refine(sys, state, target, plan.Node, TransferPlanner.Horizon(plan, now), rp, 25, out _);
                }
                return (plan, refined);
            });
        }

        void UpdateTransfer()
        {
            if (transferTask == null || !transferTask.IsCompleted) return;
            var task = transferTask;
            transferTask = null;
            if (task.IsFaulted)
            {
                TransferStatus = Loc.T("Ошибка расчёта перелёта: ") + task.Exception?.GetBaseException().Message;
                Debug.LogWarning("[SpaceSim] " + TransferStatus);
                return;
            }
            Transfer = task.Result.plan;
            if (!Transfer.Ok)
            {
                TransferStatus = Transfer.Error ?? Loc.T("Перелёт не найден.");
                return;
            }
            TransferStatus = Loc.T("Окно рассчитано.");
            if (!transferMakeNode) return;
            ManeuverNode node = task.Result.refined ?? Transfer.Node;
            if (node.Time <= Now + 1.0)
            {
                TransferStatus = Loc.T("Окно уже наступает — пересчитайте через минуту.");
                return;
            }
            CreateNode(node);
            TransferStatus = task.Result.refined != null
                ? Loc.T("Узел в окне создан и уточнён по прогнозу.")
                : Loc.T("Узел в окне создан (решение Ламберта; уточните его ручками).");
        }

        void Update()
        {
            if (boot == null || nav == null) return;
            UpdateTransfer();
            UpdateBurn();
            UpdateWarp();
            UpdatePreview();
            if (Selected.Valid && Selected.Time < Now) ClearSelection();
            if (FlightInput.IsTyping) return;
            if (FlightInput.GetKeyDown(InputKey.WarpToTarget))
            {
                if (Warping) CancelWarp();
                else if (HasSelection) WarpToSelected();
                else if (Node != null) WarpToNode();
            }
        }
    }
}
