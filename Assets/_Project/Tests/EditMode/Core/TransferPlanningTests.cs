using System;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Navigation;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Orbital flight planning: time of flight to a point of an ellipse / hyperbola, manoeuvre nodes in the orbital frame, burn cost,
    /// Lambert's problem against textbook and Hohmann values, interplanetary transfer planning and the warp-to-point stop logic.
    /// </summary>
    public class TransferPlanningTests
    {
        const double Mu = TestPlanets.EarthMu;
        const double Day = 86400.0;

        // ---- Lambert -------------------------------------------------------------------------------------------------

        [Test]
        public void Lambert_CurtisExample5_2()
        {
            const double mu = 398600.0e9;
            var r1 = new Vector3d(5000e3, 10000e3, 2100e3);
            var r2 = new Vector3d(-14600e3, 2500e3, 7000e3);
            Assert.IsTrue(LambertSolver.Solve(mu, r1, r2, 3600.0, Vector3d.UnitZ, true, out Vector3d v1, out Vector3d v2));
            Assert.AreEqual(-5992.5, v1.x, 1.0);
            Assert.AreEqual(1925.4, v1.y, 1.0);
            Assert.AreEqual(3245.6, v1.z, 1.0);
            Assert.AreEqual(-3312.5, v2.x, 1.0);
            Assert.AreEqual(-4196.6, v2.y, 1.0);
            Assert.AreEqual(-385.29, v2.z, 1.0);
        }

        [Test]
        public void Lambert_SolutionReachesTheTarget_EllipseAndHyperbola()
        {
            var r1 = new Vector3d(7.0e6, 0, 0);
            var r2 = new Vector3d(-3.0e6, 9.0e6, 1.0e6);
            foreach (double tof in new[] { 600.0, 2400.0, 9000.0 })
            {
                Assert.IsTrue(LambertSolver.Solve(Mu, r1, r2, tof, Vector3d.UnitZ, true, out Vector3d v1, out Vector3d v2), "tof " + tof);
                ConicPropagator.Propagate(Mu, r1, v1, tof, out Vector3d r, out Vector3d v);
                Assert.Less((r - r2).Magnitude, 1.0, "lands on r₂ after " + tof + " s");
                Assert.Less((v - v2).Magnitude, 1e-3);
            }
        }

        [Test]
        public void Lambert_NearHalfTurnMatchesHohmann()
        {
            const double au = 1.495978707e11, muSun = 1.32712440018e20;
            double r1 = au, r2 = 1.524 * au;
            HohmannTransfer h = TransferCalculator.Hohmann(muSun, r1, r2);
            // Target 179.5° away, arriving after the Hohmann time scaled to the shorter sweep: within 1 % of Hohmann.
            double sweep = 179.5 * MathD.DegToRad;
            var a = new Vector3d(r1, 0, 0);
            var b = new Vector3d(r2 * Math.Cos(sweep), r2 * Math.Sin(sweep), 0);
            double best = double.MaxValue, bestArr = 0.0;
            for (double k = 0.97; k <= 1.03; k += 0.0005)
            {
                if (!LambertSolver.Solve(muSun, a, b, h.TransferTime * k, Vector3d.UnitZ, true, out Vector3d v1, out Vector3d v2)) continue;
                double dv1 = (v1 - new Vector3d(0, Math.Sqrt(muSun / r1), 0)).Magnitude;
                double vc2 = Math.Sqrt(muSun / r2);
                double dv2 = (new Vector3d(-vc2 * Math.Sin(sweep), vc2 * Math.Cos(sweep), 0) - v2).Magnitude;
                if (dv1 + dv2 < best)
                {
                    best = dv1 + dv2;
                    bestArr = dv2;
                }
            }
            Assert.AreEqual(h.TotalDeltaV, best, 0.01 * h.TotalDeltaV, "Lambert minimum ≈ Hohmann Δv₁+Δv₂ (" + h.TotalDeltaV + ")");
            Assert.AreEqual(h.DeltaV2, bestArr, 0.05 * h.DeltaV2);
            Assert.AreEqual(2945.0, h.DeltaV1, 15.0, "Earth → Mars Hohmann Δv₁ ≈ 2.94 km/s");
        }

        // ---- Transfers with the real system ----------------------------------------------------------------------

        static CoastState Parking(PatchedConicSystem sys, int body, double altitude, double time)
        {
            double r = sys[body].Radius + altitude, v = Math.Sqrt(sys.Mu(body) / r);
            // In the body's orbital plane (the system's planets lie in z = 0).
            return new CoastState(body, new Vector3d(r, 0, 0), new Vector3d(0, v, 0), time);
        }

        [Test]
        public void TransferPlan_PlanetToPlanet_LambertNearHohmann_AndRefinedNodeEntersTheTargetSphere()
        {
            PatchedConicSystem sys = PatchedConicSystem.FromHomeSystem();
            int home = sys.IndexOf(HomeSystemCatalog.HomeWorldId), tholin = sys.IndexOf(HomeSystemCatalog.TholinId);
            double now = 3.0 * Day;
            CoastState s = Parking(sys, home, 300000.0, now);
            TransferPlan plan = TransferPlanner.Plan(sys, s, tholin, now);
            Assert.IsTrue(plan.Ok, plan.Error);
            Assert.AreEqual(TransferKind.Escape, plan.Kind);
            Assert.Greater(plan.Node.Time, now);
            Assert.AreEqual(plan.HohmannDeltaV1, plan.DepartureDeltaV, 0.15 * plan.HohmannDeltaV1, "Lambert departure ≈ Level A Hohmann");
            Assert.AreEqual(plan.HohmannTime, plan.TimeOfFlight, 0.3 * plan.HohmannTime);
            Assert.Greater(plan.Node.Prograde, 0.9 * plan.Node.Magnitude, "mostly prograde");

            // The unrefined node already leaves the planet and crosses the target's orbit.
            double horizon = TransferPlanner.Horizon(plan, now);
            TrajectoryPrediction raw = TrajectoryPredictor.Predict(sys, s, horizon, tholin, plan.Node, 100);
            Assert.IsTrue(raw.HasHeliocentric, "escapes the home sphere");
            Assert.Less(raw.ClosestDistance, 0.05 * sys[tholin].Orbit.SemiMajorAxis, "passes close to the target before fine tuning");

            ManeuverNode refined = TransferPlanner.Refine(sys, s, tholin, plan.Node, horizon, sys[tholin].Radius + 200000.0, 30, out TrajectoryPrediction p);
            Assert.IsNotNull(refined);
            Assert.IsTrue(p.Encounter, "the refined node enters the target sphere (closest " + p.ClosestDistance + " m)");
            Assert.AreEqual(plan.Node.Magnitude, refined.Magnitude, 0.1 * plan.Node.Magnitude, "fine tuning stays near the Lambert burn");
        }

        [Test]
        public void TransferPlan_PlanetToMoon_IsDirectLambert_AndPredictedEncounter()
        {
            PatchedConicSystem sys = PatchedConicSystem.FromHomeSystem();
            int home = sys.IndexOf(HomeSystemCatalog.HomeWorldId), moon = sys.IndexOf(HomeSystemCatalog.KolimpriId);
            double now = 1.0 * Day;
            // Parking orbit in the moon's orbital plane.
            Vector3d rm = sys.Position(moon, now) - sys.Position(home, now), vm = sys.Velocity(moon, now) - sys.Velocity(home, now);
            Vector3d n = Vector3d.Cross(rm, vm).Normalized;
            Vector3d x = Vector3d.Cross(n, Vector3d.UnitX).SqrMagnitude > 1e-6 ? Vector3d.Cross(Vector3d.Cross(n, Vector3d.UnitX), n).Normalized : Vector3d.UnitY;
            double r = sys[home].Radius + 300000.0, v = Math.Sqrt(sys.Mu(home) / r);
            var s = new CoastState(home, x * r, Vector3d.Cross(n, x) * v, now);
            TransferPlan plan = TransferPlanner.Plan(sys, s, moon, now);
            Assert.IsTrue(plan.Ok, plan.Error);
            Assert.AreEqual(TransferKind.Direct, plan.Kind);
            Assert.AreEqual(plan.HohmannDeltaV1, plan.DepartureDeltaV, 0.1 * plan.HohmannDeltaV1);
            TrajectoryPrediction p = TrajectoryPredictor.Predict(sys, s, TransferPlanner.Horizon(plan, now), moon, plan.Node);
            Assert.IsTrue(p.Encounter, "the Lambert arc aims at the moon's centre: it must enter its sphere (closest " + p.ClosestDistance + " m)");
        }

        [Test]
        public void TransferPlan_RefusesUnreachableTargets()
        {
            PatchedConicSystem sys = PatchedConicSystem.FromHomeSystem();
            int home = sys.IndexOf(HomeSystemCatalog.HomeWorldId), moraMoon = sys.IndexOf(HomeSystemCatalog.MoraMoonId);
            TransferPlan p = TransferPlanner.Plan(sys, Parking(sys, home, 300000.0, 0.0), moraMoon, 0.0);
            Assert.IsFalse(p.Ok);
            Assert.IsNotNull(p.Error);
        }

        // ---- Warp to a point ------------------------------------------------------------------------------------------

        [Test]
        public void WarpApproach_SlowsDown_StopsAtThePoint_AndRespectsTheLimit()
        {
            var rng = new Random(7);
            foreach ((double start, double limit) in new[] { (30.0, 4.0), (5.0e3, 4.0), (5.0e3, 1000.0), (3.0e7, 1.0e6), (2.0e5, 1.0e5) })
                {
                    double remaining = start, previousWarp = double.MaxValue, real = 0.0;
                    bool arrived = false;
                    int frames = 0;
                    while (!arrived && frames < 1000000)
                    {
                        double warp = WarpApproach.Command(remaining, limit, 0.1, out arrived);
                        Assert.LessOrEqual(warp, limit + 1e-9);
                        Assert.GreaterOrEqual(warp, 1.0);
                        if (arrived) break;
                        if (warp < limit) Assert.LessOrEqual(warp, previousWarp + 1e-9, "once below the limit the warp only decreases");
                        previousWarp = warp;
                        double dt = 1.0 / 60.0 + rng.NextDouble() * (0.1 - 1.0 / 60.0) * (rng.NextDouble() < 0.05 ? 1.0 : 0.0);
                        double advance = warp * dt;
                        Assert.Less(advance, remaining + dt, "a frame never jumps far past the point");
                        remaining -= advance;
                        real += dt;
                        frames++;
                    }
                    Assert.IsTrue(arrived, "arrives (start " + start + ", limit " + limit + ")");
                    Assert.GreaterOrEqual(remaining, -0.1, "stops at the point, not after it (×1 for the last frames)");
                    double ideal = start / limit;
                    Assert.Less(real, ideal + 30.0, "no needless crawling");
                }
        }

        [Test]
        public void WarpApproach_ArrivedWhenPassed_AndNodeLeadIsHalfBurnPlusMargin()
        {
            WarpApproach.Command(-3.0, 1000.0, 0.1, out bool passed);
            Assert.IsTrue(passed);
            Assert.AreEqual(1.0, WarpApproach.Command(0.01, 1000.0, 0.1, out bool reached));
            Assert.IsTrue(reached);
            Assert.AreEqual(80.0, WarpApproach.NodeLead(100.0, 30.0), 1e-9);
            Assert.AreEqual(30.0, WarpApproach.NodeLead(double.PositiveInfinity, 30.0), 1e-9);
        }

        [Test]
        public void BurnControl_ThrottlesDown_CutsOffAndDetectsOvershoot()
        {
            Assert.AreEqual(1.0, BurnControl.Throttle(500.0, 10.0), 1e-12);
            Assert.AreEqual(0.5, BurnControl.Throttle(5.0, 10.0), 1e-12);
            Assert.AreEqual(BurnControl.MinThrottle, BurnControl.Throttle(0.2, 10.0), 1e-12);
            Assert.AreEqual(0.0, BurnControl.Throttle(0.05, 10.0), 1e-12);
            var dir = new Vector3d(1, 0, 0);
            Assert.IsFalse(BurnControl.Finished(new Vector3d(3, 0, 0), dir));
            Assert.IsTrue(BurnControl.Finished(new Vector3d(0.05, 0, 0), dir));
            Assert.IsTrue(BurnControl.Finished(new Vector3d(-1, 0, 0), dir), "overshoot: stop");
            Assert.AreEqual(350.0, BurnControl.Approach(10.0, 350.0, 30.0, true), 1e-9, "heading goes the short way");
            Assert.AreEqual(40.0, BurnControl.Approach(10.0, 90.0, 30.0, false), 1e-9);
            Assert.AreEqual(90.0, BurnControl.AttitudeError(0.0, 0.0, 90.0, 45.0), 1e-9);

            // Simulated burn at ×1 with the throttle law: ends within the cutoff, never overshoots by more than one frame.
            double remaining = 50.0, a = 8.0, dt = 1.0 / 60.0;
            int frames = 0;
            while (!BurnControl.Finished(new Vector3d(remaining, 0, 0), dir) && frames++ < 100000)
                remaining -= BurnControl.Throttle(remaining, a) * a * dt;
            Assert.Less(Math.Abs(remaining), BurnControl.CutoffDeltaV + a * dt * BurnControl.MinThrottle);
        }
    }
}
