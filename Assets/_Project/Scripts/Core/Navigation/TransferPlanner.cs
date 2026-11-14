using System;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Navigation
{
    public enum TransferKind
    {
        /// <summary>No transfer possible (see <see cref="TransferPlan.Error"/>).</summary>
        None,
        /// <summary>The target orbits the vehicle's own primary (planet → its moon, star → a planet): one Lambert arc from the vehicle's orbit.</summary>
        Direct,
        /// <summary>The target orbits the same body as the vehicle's primary (planet → planet): leave on a hyperbola, Lambert arc around the common parent.</summary>
        Escape
    }

    /// <summary>A transfer to a destination body: Level A (Hohmann) reference numbers, the Lambert solution with the real moving bodies, and the node.</summary>
    public sealed class TransferPlan
    {
        public TransferKind Kind;
        public string Error;
        public int Origin, Target;
        /// <summary>Body both ends of the heliocentric (or planetocentric) arc orbit.</summary>
        public int Central;

        // Level A: Hohmann between circular orbits.
        public double HohmannDeltaV1, HohmannDeltaV2, HohmannTime;
        /// <summary>Phase angle the target must lead the origin by at departure [rad].</summary>
        public double RequiredPhase;
        public double CurrentPhase;
        public double TimeToWindow, WindowTime, SynodicPeriod;

        // Lambert with the bodies' real positions.
        public double DepartureTime, TimeOfFlight;
        public double ArrivalTime => DepartureTime + TimeOfFlight;
        /// <summary>Hyperbolic excess speeds (Escape) or relative speeds (Direct) at both ends [m/s].</summary>
        public double DepartureExcess, ArrivalExcess;
        /// <summary>Δv of the departure burn from the vehicle's orbit [m/s].</summary>
        public double DepartureDeltaV;
        /// <summary>Δv to capture into a circular orbit at the capture altitude on arrival [m/s].</summary>
        public double CaptureDeltaV;
        public double TotalDeltaV => DepartureDeltaV + CaptureDeltaV;
        /// <summary>The node at the window (feeds the same node system as a node set by hand).</summary>
        public ManeuverNode Node;

        public bool Ok => Error == null && Node != null;
    }

    /// <summary>
    /// Interplanetary (and planet → moon) transfer planning. Level A numbers from <see cref="TransferCalculator"/> (Hohmann, phase
    /// angle, synodic period, departure/capture), then a Lambert search (<see cref="LambertSolver"/>) over departure time and time of
    /// flight with the bodies' real Kepler positions, and the departure burn on the vehicle's actual orbit. The node can then be
    /// checked and fine-tuned with the patched-conic <see cref="TrajectoryPredictor"/> (<see cref="Refine"/>).
    /// </summary>
    public static class TransferPlanner
    {
        const int TimeSteps = 49, FlightSteps = 31;

        public static TransferPlan Plan(PatchedConicSystem sys, CoastState vehicle, int target, double now, double captureAltitude = 200000.0)
        {
            var plan = new TransferPlan { Target = target, Origin = vehicle.Primary };
            if (sys == null || target < 0 || target >= sys.Count)
                return Fail(plan, Loc.T("Выберите цель — другое тело."));
            if (target == vehicle.Primary)
                return Fail(plan, Loc.T("Аппарат уже в сфере влияния цели."));
            NavBody t = sys[target];
            if (t.ParentIndex == vehicle.Primary) return PlanDirect(sys, vehicle, target, now, captureAltitude, plan);
            if (vehicle.Primary >= 0 && t.ParentIndex == sys.Parent(vehicle.Primary)) return PlanEscape(sys, vehicle, target, now, captureAltitude, plan);
            return Fail(plan, Loc.T("Перелёт рассчитывается к телу, которое обращается вокруг того же центра, что и ") +
                              sys.NameOf(vehicle.Primary) + Loc.T(", или вокруг него самого: сначала долетите до ") + sys.NameOf(t.ParentIndex) + ".");
        }

        static TransferPlan Fail(TransferPlan p, string error)
        {
            p.Kind = TransferKind.None;
            p.Error = error;
            return p;
        }

        static double Period(double mu, double a) => MathD.TwoPi * Math.Sqrt(a * a * a / mu);

        // ---- Planet → planet: hyperbolic escape, heliocentric Lambert arc ---------------------------------------------

        static TransferPlan PlanEscape(PatchedConicSystem sys, CoastState vehicle, int target, double now, double captureAltitude, TransferPlan plan)
        {
            int origin = vehicle.Primary;
            NavBody o = sys[origin], t = sys[target];
            int central = o.ParentIndex;
            double muC = sys.Mu(central);
            plan.Kind = TransferKind.Escape;
            plan.Central = central;
            double muO = o.Mu;
            double alpha = 2.0 / vehicle.Position.Magnitude - vehicle.Velocity.SqrMagnitude / muO;
            if (!(alpha > 0.0)) return Fail(plan, Loc.T("Аппарат уже покидает сферу влияния: перелёт считается с замкнутой орбиты ожидания."));
            double parkingRadius = 1.0 / alpha;

            double r1 = o.Orbit.SemiMajorAxis, r2 = t.Orbit.SemiMajorAxis;
            HohmannTransfer h = TransferCalculator.Hohmann(muC, r1, r2);
            plan.HohmannTime = h.TransferTime;
            plan.HohmannDeltaV1 = TransferCalculator.DepartureDeltaV(muO, parkingRadius, h.DeltaV1);
            plan.HohmannDeltaV2 = TransferCalculator.CaptureDeltaV(t.Mu, t.Radius + captureAltitude, h.DeltaV2);
            plan.RequiredPhase = TransferCalculator.HohmannPhaseAngle(muC, r1, r2);
            plan.CurrentPhase = PhaseAround(sys, central, origin, target, now);
            double n1 = MathD.TwoPi / o.Orbit.Period, n2 = MathD.TwoPi / t.Orbit.Period;
            plan.TimeToWindow = TransferCalculator.TimeToPhaseAngle(plan.CurrentPhase, plan.RequiredPhase, n1, n2);
            plan.WindowTime = now + plan.TimeToWindow;
            plan.SynodicPeriod = TransferCalculator.SynodicPeriod(o.Orbit.Period, t.Orbit.Period);

            // Lambert grid around the Level A window: departure ± a tenth of the synodic period (at most a quarter orbit of the
            // origin), time of flight 0.6…1.4 × Hohmann. Cost: departure + arrival excess speeds.
            Vector3d refNormal = Vector3d.Cross(sys.Position(origin, now) - sys.Position(central, now), sys.Velocity(origin, now) - sys.Velocity(central, now));
            double span = Math.Min(0.1 * plan.SynodicPeriod, 0.25 * o.Orbit.Period);
            double earliest = now + 0.5 * Period(muO, parkingRadius);
            double Cost(double td, double tof, out Vector3d vInfDep, out Vector3d vInfArr)
            {
                vInfDep = vInfArr = Vector3d.Zero;
                if (td < earliest || tof <= 0.0) return double.PositiveInfinity;
                Vector3d ro = sys.Position(origin, td) - sys.Position(central, td);
                Vector3d rt = sys.Position(target, td + tof) - sys.Position(central, td + tof);
                if (!LambertSolver.Solve(muC, ro, rt, tof, refNormal, true, out Vector3d v1, out Vector3d v2)) return double.PositiveInfinity;
                vInfDep = v1 - (sys.Velocity(origin, td) - sys.Velocity(central, td));
                vInfArr = v2 - (sys.Velocity(target, td + tof) - sys.Velocity(central, td + tof));
                return vInfDep.Magnitude + vInfArr.Magnitude;
            }
            if (!Search(plan.WindowTime, span, h.TransferTime, (td, tof) => Cost(td, tof, out _, out _), out double bestTd, out double bestTof))
                return Fail(plan, Loc.T("Решение задачи Ламберта не найдено."));
            Cost(bestTd, bestTof, out Vector3d vInf, out Vector3d vInfArrival);
            plan.DepartureTime = bestTd;
            plan.TimeOfFlight = bestTof;
            plan.DepartureExcess = vInf.Magnitude;
            plan.ArrivalExcess = vInfArrival.Magnitude;
            plan.CaptureDeltaV = TransferCalculator.CaptureDeltaV(t.Mu, t.Radius + captureAltitude, plan.ArrivalExcess);

            plan.Node = EscapeNode(sys, vehicle, now, bestTd, vInf);
            if (plan.Node == null) return Fail(plan, Loc.T("Не удалось найти точку отлёта на орбите ожидания."));

            // Differential correction through the patched conics: fly the escape hyperbola to the sphere's edge, solve Lambert from
            // there to the same arrival, and shift the aimed v∞ by the velocity error (the plane change, the time spent climbing out
            // and the planet's motion meanwhile are all accounted for by the predictor).
            double arrival = bestTd + bestTof;
            Vector3d aim = vInf;
            for (int iter = 0; iter < 6; iter++)
            {
                double climb = 3.0 * o.SoiRadius / Math.Max(100.0, aim.Magnitude) + 86400.0;
                TrajectoryPrediction pr = TrajectoryPredictor.Predict(sys, vehicle, plan.Node.Time - vehicle.Time + climb, PatchedConicSystem.Star, plan.Node, 40);
                if (!pr.HasHeliocentric || pr.Heliocentric.Primary != central) break;
                CoastState exit = pr.Heliocentric;
                double left = arrival - exit.Time;
                if (left <= 0.0) break;
                Vector3d rt = sys.Position(target, arrival) - sys.Position(central, arrival);
                Vector3d rExit = exit.Position + sys.Position(exit.Primary, exit.Time) - sys.Position(central, exit.Time);
                if (!LambertSolver.Solve(muC, rExit, rt, left, refNormal, true, out Vector3d vReq, out Vector3d vArr)) break;
                Vector3d vExit = exit.Velocity + sys.Velocity(exit.Primary, exit.Time) - sys.Velocity(central, exit.Time);
                Vector3d error = vReq - vExit;
                if (error.Magnitude < 0.02) break;
                aim += error;
                ManeuverNode corrected = EscapeNode(sys, vehicle, now, bestTd, aim);
                if (corrected == null) break;
                plan.Node = corrected;
                plan.ArrivalExcess = (vArr - (sys.Velocity(target, arrival) - sys.Velocity(central, arrival))).Magnitude;
            }
            plan.CaptureDeltaV = TransferCalculator.CaptureDeltaV(t.Mu, t.Radius + captureAltitude, plan.ArrivalExcess);
            plan.DepartureDeltaV = plan.Node.Magnitude;
            return plan;
        }

        /// <summary>
        /// The departure burn on the parking orbit that leaves along a hyperbola whose asymptote points along v∞: the burn point lies
        /// θ∞ = arccos(−1/e) before the asymptote direction (in the direction of motion), the speed there is √(v∞² + 2μ/r).
        /// </summary>
        public static ManeuverNode EscapeNode(PatchedConicSystem sys, CoastState vehicle, double now, double departureTime, Vector3d vInf)
        {
            double mu = sys.Mu(vehicle.Primary);
            Vector3d hVec = Vector3d.Cross(vehicle.Position, vehicle.Velocity);
            if (!(hVec.SqrMagnitude > 0.0) || !(vInf.SqrMagnitude > 0.0)) return null;
            Vector3d n = hVec.Normalized;
            Vector3d u = Vector3d.ProjectOnPlane(vInf, n);
            if (!(u.SqrMagnitude > 1e-12 * vInf.SqrMagnitude)) u = vehicle.Velocity; // v∞ along the orbit normal: any point will do
            u = u.Normalized;
            double vInfMag = vInf.Magnitude;

            // Iterate: the burn radius sets e and θ∞, which move the burn point (non-circular parking orbits).
            double r = vehicle.Position.Magnitude;
            double tof = 0.0;
            Vector3d burnDir = u;
            for (int k = 0; k < 4; k++)
            {
                double e = 1.0 + r * vInfMag * vInfMag / mu;
                double thetaInf = Math.Acos(-1.0 / e);
                burnDir = u * Math.Cos(thetaInf) - Vector3d.Cross(n, u) * Math.Sin(thetaInf);
                tof = TrajectoryPoints.TimeOfFlight(mu, vehicle.Position, vehicle.Velocity, burnDir, Math.Max(0.0, departureTime - vehicle.Time));
                if (double.IsInfinity(tof)) return null;
                ConicPropagator.Propagate(mu, vehicle.Position, vehicle.Velocity, tof, out Vector3d rb, out _);
                r = rb.Magnitude;
            }
            ConicPropagator.Propagate(mu, vehicle.Position, vehicle.Velocity, tof, out Vector3d rBurn, out Vector3d vBurn);
            double speed = Math.Sqrt(vInfMag * vInfMag + 2.0 * mu / rBurn.Magnitude);
            // Velocity at the hyperbola's periapsis: perpendicular to the radius, in the plane of the radius and v∞.
            Vector3d rHat = rBurn.Normalized;
            Vector3d dir = vInf - rHat * Vector3d.Dot(vInf, rHat);
            if (!(dir.SqrMagnitude > 0.0)) dir = vBurn;
            Vector3d wanted = dir.Normalized * speed;
            return ManeuverPlanning.NodeFromVector(vehicle.Time + tof, rBurn, vBurn, wanted - vBurn);
        }

        // ---- Within one sphere: the vehicle's orbit → a body orbiting the same primary -------------------------------

        static TransferPlan PlanDirect(PatchedConicSystem sys, CoastState vehicle, int target, double now, double captureAltitude, TransferPlan plan)
        {
            int central = vehicle.Primary;
            NavBody t = sys[target];
            double mu = sys.Mu(central);
            plan.Kind = TransferKind.Direct;
            plan.Central = central;
            double alpha = 2.0 / vehicle.Position.Magnitude - vehicle.Velocity.SqrMagnitude / mu;
            double r1 = alpha > 0.0 ? 1.0 / alpha : vehicle.Position.Magnitude;
            double r2 = t.Orbit.SemiMajorAxis;
            HohmannTransfer h = TransferCalculator.Hohmann(mu, r1, r2);
            plan.HohmannTime = h.TransferTime;
            plan.HohmannDeltaV1 = h.DeltaV1;
            plan.HohmannDeltaV2 = TransferCalculator.CaptureDeltaV(t.Mu, t.Radius + captureAltitude, h.DeltaV2);
            plan.RequiredPhase = TransferCalculator.HohmannPhaseAngle(mu, r1, r2);
            Vector3d tr = sys.Position(target, now) - sys.Position(central, now);
            Vector3d hv = Vector3d.Cross(vehicle.Position, vehicle.Velocity);
            plan.CurrentPhase = TrajectoryPoints.SignedAngle(vehicle.Position, Vector3d.ProjectOnPlane(tr, hv.Normalized), hv.Normalized);
            double vehiclePeriod = alpha > 0.0 ? Period(mu, r1) : double.PositiveInfinity;
            double n1 = alpha > 0.0 ? MathD.TwoPi / vehiclePeriod : 0.0, n2 = MathD.TwoPi / t.Orbit.Period;
            plan.TimeToWindow = TransferCalculator.TimeToPhaseAngle(plan.CurrentPhase, plan.RequiredPhase, n1, n2);
            plan.WindowTime = now + (double.IsInfinity(plan.TimeToWindow) ? 0.0 : plan.TimeToWindow);
            plan.SynodicPeriod = alpha > 0.0 ? TransferCalculator.SynodicPeriod(vehiclePeriod, t.Orbit.Period) : double.PositiveInfinity;

            double span = double.IsInfinity(vehiclePeriod) ? 0.25 * h.TransferTime : 0.5 * Math.Min(vehiclePeriod, plan.SynodicPeriod);
            double earliest = now + 60.0;
            double Cost(double td, double tof, out Vector3d dv1, out Vector3d vRel)
            {
                dv1 = vRel = Vector3d.Zero;
                if (td < earliest || tof <= 0.0) return double.PositiveInfinity;
                ConicPropagator.Propagate(mu, vehicle.Position, vehicle.Velocity, td - vehicle.Time, out Vector3d rv, out Vector3d vv);
                Vector3d rt = sys.Position(target, td + tof) - sys.Position(central, td + tof);
                if (!LambertSolver.Solve(mu, rv, rt, tof, hv, true, out Vector3d v1, out Vector3d v2)) return double.PositiveInfinity;
                dv1 = v1 - vv;
                vRel = v2 - (sys.Velocity(target, td + tof) - sys.Velocity(central, td + tof));
                return dv1.Magnitude + vRel.Magnitude;
            }
            if (!Search(Math.Max(plan.WindowTime, earliest + span), span, h.TransferTime, (td, tof) => Cost(td, tof, out _, out _), out double bestTd, out double bestTof))
                return Fail(plan, Loc.T("Решение задачи Ламберта не найдено."));
            Cost(bestTd, bestTof, out Vector3d dv, out Vector3d rel);
            plan.DepartureTime = bestTd;
            plan.TimeOfFlight = bestTof;
            plan.DepartureExcess = dv.Magnitude;
            plan.ArrivalExcess = rel.Magnitude;
            plan.CaptureDeltaV = TransferCalculator.CaptureDeltaV(t.Mu, t.Radius + captureAltitude, plan.ArrivalExcess);
            ConicPropagator.Propagate(mu, vehicle.Position, vehicle.Velocity, bestTd - vehicle.Time, out Vector3d rN, out Vector3d vN);
            plan.Node = ManeuverPlanning.NodeFromVector(bestTd, rN, vN, dv);
            plan.DepartureDeltaV = plan.Node.Magnitude;
            return plan;
        }

        /// <summary>Signed angle of body b ahead of body a around their common parent (in a's direction of motion) [rad].</summary>
        static double PhaseAround(PatchedConicSystem sys, int central, int a, int b, double time)
        {
            Vector3d c = sys.Position(central, time), vc = sys.Velocity(central, time);
            Vector3d ra = sys.Position(a, time) - c, rb = sys.Position(b, time) - c;
            Vector3d n = Vector3d.Cross(ra, sys.Velocity(a, time) - vc).Normalized;
            return TrajectoryPoints.SignedAngle(ra, Vector3d.ProjectOnPlane(rb, n), n);
        }

        /// <summary>Grid over (departure time, time of flight) around (center, Hohmann time), then a shrinking pattern search.</summary>
        static bool Search(double center, double span, double hohmannTime, Func<double, double, double> cost, out double bestTd, out double bestTof)
        {
            bestTd = center;
            bestTof = hohmannTime;
            double best = double.PositiveInfinity;
            for (int i = 0; i < TimeSteps; i++)
            {
                double td = center - span + 2.0 * span * i / (TimeSteps - 1);
                for (int j = 0; j < FlightSteps; j++)
                {
                    double tof = hohmannTime * (0.6 + 0.8 * j / (FlightSteps - 1));
                    double c = cost(td, tof);
                    if (c < best)
                    {
                        best = c;
                        bestTd = td;
                        bestTof = tof;
                    }
                }
            }
            if (double.IsInfinity(best)) return false;
            double dt = 2.0 * span / (TimeSteps - 1), df = 0.8 * hohmannTime / (FlightSteps - 1);
            for (int k = 0; k < 80 && (dt > 1.0 || df > 1.0); k++)
            {
                bool improved = false;
                foreach ((double a, double b) in new[] { (dt, 0.0), (-dt, 0.0), (0.0, df), (0.0, -df) })
                {
                    double c = cost(bestTd + a, bestTof + b);
                    if (c < best)
                    {
                        best = c;
                        bestTd += a;
                        bestTof += b;
                        improved = true;
                        break;
                    }
                }
                if (!improved)
                {
                    dt *= 0.5;
                    df *= 0.5;
                }
            }
            return true;
        }

        // ---- Fine tuning against the patched-conic prediction -----------------------------------------------------------

        /// <summary>
        /// Adjusts the node (time, prograde, normal, radial) by a shrinking pattern search so the patched-conic prediction enters the
        /// target's sphere of influence with the periapsis closest to <paramref name="targetPeriapsis"/> [m from the centre].
        /// Each step costs one prediction: run it off the main thread.
        /// </summary>
        public static ManeuverNode Refine(PatchedConicSystem sys, CoastState vehicle, int target, ManeuverNode node, double horizon, double targetPeriapsis,
            int maxIterations, out TrajectoryPrediction prediction)
        {
            prediction = null;
            if (node == null) return null;
            int origin = vehicle.Primary;
            double Cost(ManeuverNode n, out TrajectoryPrediction pr)
            {
                pr = TrajectoryPredictor.Predict(sys, vehicle, horizon, target, n, 100);
                if (pr.Impact && pr.ImpactBody == origin) return double.MaxValue;
                if (pr.Encounter) return Math.Abs(pr.ArrivalPeriapsis - targetPeriapsis);
                return sys[target].SoiRadius + pr.ClosestDistance;
            }
            ManeuverNode best = node.Clone();
            double bestCost = Cost(best, out prediction);
            double mag = Math.Max(1.0, node.Magnitude);
            double dt = 120.0, dv = 0.01 * mag;
            for (int iter = 0; iter < maxIterations && (dt > 0.5 || dv > 0.02); iter++)
            {
                bool improved = false;
                foreach (ManeuverNode trial in Neighbours(best, dt, dv))
                {
                    if (trial.Time < vehicle.Time) continue;
                    double c = Cost(trial, out TrajectoryPrediction pr);
                    if (c < bestCost)
                    {
                        bestCost = c;
                        best = trial;
                        prediction = pr;
                        improved = true;
                    }
                }
                if (!improved)
                {
                    dt *= 0.5;
                    dv *= 0.5;
                }
            }
            return best;
        }

        static ManeuverNode[] Neighbours(ManeuverNode n, double dt, double dv) => new[]
        {
            new ManeuverNode { Time = n.Time + dt, Prograde = n.Prograde, Normal = n.Normal, Radial = n.Radial },
            new ManeuverNode { Time = n.Time - dt, Prograde = n.Prograde, Normal = n.Normal, Radial = n.Radial },
            new ManeuverNode { Time = n.Time, Prograde = n.Prograde + dv, Normal = n.Normal, Radial = n.Radial },
            new ManeuverNode { Time = n.Time, Prograde = n.Prograde - dv, Normal = n.Normal, Radial = n.Radial },
            new ManeuverNode { Time = n.Time, Prograde = n.Prograde, Normal = n.Normal + dv, Radial = n.Radial },
            new ManeuverNode { Time = n.Time, Prograde = n.Prograde, Normal = n.Normal - dv, Radial = n.Radial },
            new ManeuverNode { Time = n.Time, Prograde = n.Prograde, Normal = n.Normal, Radial = n.Radial + dv },
            new ManeuverNode { Time = n.Time, Prograde = n.Prograde, Normal = n.Normal, Radial = n.Radial - dv },
        };

        /// <summary>Prediction horizon that covers the node and the whole transfer.</summary>
        public static double Horizon(TransferPlan plan, double now) =>
            Math.Max(0.0, plan.Node != null ? plan.Node.Time - now : 0.0) + 1.5 * Math.Max(plan.TimeOfFlight, plan.HohmannTime) + 2.0 * 86400.0;
    }
}
