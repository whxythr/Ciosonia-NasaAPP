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
    public class FlightPlanningTests
    {
        const double Mu = TestPlanets.EarthMu;
        const double Day = 86400.0;

        static double MeanAnomalyFromTrue(double e, double theta)
        {
            double E = 2.0 * Math.Atan(Math.Sqrt((1.0 - e) / (1.0 + e)) * Math.Tan(theta / 2.0));
            return E - e * Math.Sin(E);
        }

        // ---- Time of flight -----------------------------------------------------------------------------------------

        [Test]
        public void TimeOfFlight_Ellipse_MatchesKeplerEquation_AndPropagatorLandsOnThePoint()
        {
            double rp = 7.0e6, ra = 2.1e7, a = 0.5 * (rp + ra), e = (ra - rp) / (ra + rp);
            double vp = Math.Sqrt(Mu * (2.0 / rp - 1.0 / a));
            var r0 = new Vector3d(rp, 0, 0);
            var v0 = new Vector3d(0, vp, 0);
            double n = Math.Sqrt(Mu / (a * a * a));
            foreach (double thetaDeg in new[] { 30.0, 90.0, 179.0, 250.0, 330.0 })
            {
                double theta = thetaDeg * MathD.DegToRad;
                var dir = new Vector3d(Math.Cos(theta), Math.Sin(theta), 0);
                double expected = MeanAnomalyFromTrue(e, theta);
                if (expected < 0) expected += MathD.TwoPi;
                expected /= n;
                double tof = TrajectoryPoints.TimeOfFlight(Mu, r0, v0, dir * 1234.0);
                Assert.AreEqual(expected, tof, 1e-6 * expected + 1e-6, "θ = " + thetaDeg);
                ConicPropagator.Propagate(Mu, r0, v0, tof, out Vector3d r, out _);
                Assert.Less(Vector3d.Angle(r, dir) * MathD.RadToDeg, 1e-6, "the propagated state is on the clicked direction");
            }

            // From a later state (after apoapsis), and the nearest revolution to a hint.
            double period = MathD.TwoPi / n;
            ConicPropagator.Propagate(Mu, r0, v0, 0.7 * period, out Vector3d r1, out Vector3d v1);
            double toPeri = TrajectoryPoints.TimeOfFlight(Mu, r1, v1, new Vector3d(1, 0, 0));
            Assert.AreEqual(0.3 * period, toPeri, 1e-6 * period);
            double third = TrajectoryPoints.TimeOfFlight(Mu, r1, v1, new Vector3d(1, 0, 0), 2.2 * period);
            Assert.AreEqual(2.3 * period, third, 1e-6 * period, "hint picks the pass 2 revolutions later");
        }

        [Test]
        public void TimeOfFlight_Circle_IsAngleOverMeanMotion()
        {
            double r = 7.0e6, v = Math.Sqrt(Mu / r);
            var r0 = new Vector3d(r, 0, 0);
            var v0 = new Vector3d(0, v, 0);
            double period = MathD.TwoPi * Math.Sqrt(r * r * r / Mu);
            Assert.AreEqual(0.25 * period, TrajectoryPoints.TimeOfFlight(Mu, r0, v0, new Vector3d(0, 5, 0)), 1e-6 * period);
            Assert.AreEqual(0.75 * period, TrajectoryPoints.TimeOfFlight(Mu, r0, v0, new Vector3d(0, -5, 0)), 1e-6 * period);
        }

        [Test]
        public void TimeOfFlight_Hyperbola_MatchesHyperbolicKepler_InfinityOutsideAsymptotesOrBehind()
        {
            double rp = 7.0e6, vp = 1.5 * Math.Sqrt(2.0 * Mu / rp); // e = rp·v²/μ − 1 = 3.5
            double e = rp * vp * vp / Mu - 1.0, aH = rp / (e - 1.0);
            var r0 = new Vector3d(rp, 0, 0);
            var v0 = new Vector3d(0, vp, 0);
            double nH = Math.Sqrt(Mu / (aH * aH * aH));
            foreach (double thetaDeg in new[] { 20.0, 60.0, 100.0 })
            {
                double theta = thetaDeg * MathD.DegToRad;
                double F = 2.0 * Atanh(Math.Sqrt((e - 1.0) / (e + 1.0)) * Math.Tan(theta / 2.0));
                double expected = (e * Math.Sinh(F) - F) / nH;
                double tof = TrajectoryPoints.TimeOfFlight(Mu, r0, v0, new Vector3d(Math.Cos(theta), Math.Sin(theta), 0));
                Assert.AreEqual(expected, tof, 1e-6 * expected, "θ = " + thetaDeg);
                ConicPropagator.Propagate(Mu, r0, v0, tof, out Vector3d r, out _);
                Assert.Less(Vector3d.Angle(r, new Vector3d(Math.Cos(theta), Math.Sin(theta), 0)) * MathD.RadToDeg, 1e-6);
            }
            double thetaInf = Math.Acos(-1.0 / e) * MathD.RadToDeg; // ≈ 106.6°
            Assert.IsTrue(double.IsPositiveInfinity(TrajectoryPoints.TimeOfFlight(Mu, r0, v0, Dir(thetaInf + 5.0))), "beyond the asymptote");
            Assert.IsTrue(double.IsPositiveInfinity(TrajectoryPoints.TimeOfFlight(Mu, r0, v0, Dir(-40.0))), "the incoming branch is in the past");
        }

        static Vector3d Dir(double deg) => new Vector3d(Math.Cos(deg * MathD.DegToRad), Math.Sin(deg * MathD.DegToRad), 0);
        static double Atanh(double x) => 0.5 * Math.Log((1.0 + x) / (1.0 - x));

        [Test]
        public void PointOnPatch_UsesKeplerTime_OnTheCurrentConic()
        {
            PatchedConicSystem sys = PatchedConicSystem.FromHomeSystem();
            int home = sys.IndexOf(HomeSystemCatalog.HomeWorldId);
            double mu = sys.Mu(home), r = sys[home].Radius + 400000.0;
            var s = new CoastState(home, new Vector3d(r, 0, 0), new Vector3d(0, Math.Sqrt(mu / r) * 1.05, 0), 1000.0);
            TrajectoryPrediction p = TrajectoryPredictor.Predict(sys, s, 20000.0, -1);
            TrajectoryPoint pt = TrajectoryPoints.PointOnPatch(sys, p, 0, 40, 0.37, s);
            Assert.IsTrue(pt.Valid);
            double tof = TrajectoryPoints.TimeOfFlight(mu, s.Position, s.Velocity, pt.State.Position, pt.Time - s.Time);
            Assert.AreEqual(s.Time + tof, pt.Time, 1e-6);
            Assert.IsTrue(TrajectoryPoints.StateAt(sys, p, pt.Time, out CoastState again, out int patch));
            Assert.AreEqual(0, patch);
            Assert.Less((again.Position - pt.State.Position).Magnitude, 1e-3);
        }

        // ---- Manoeuvre nodes in the orbital frame -------------------------------------------------------------------

        [Test]
        public void Node_Prograde_RaisesApoapsis_AsVisViva()
        {
            double r = 7.0e6, v = Math.Sqrt(Mu / r), dv = 500.0;
            var rv = new Vector3d(r, 0, 0);
            var vv = new Vector3d(0, v, 0);
            OrbitPreview o = ManeuverPlanning.AfterBurn(Mu, 0, rv, vv, new ManeuverNode { Prograde = dv }, Vector3d.UnitZ);
            double v2 = v + dv, a = 1.0 / (2.0 / r - v2 * v2 / Mu);
            Assert.IsTrue(o.Bound);
            Assert.AreEqual(r, o.PeriapsisRadius, 1e-3);
            Assert.AreEqual(2.0 * a - r, o.ApoapsisRadius, 1e-3 * a);
            Assert.AreEqual(MathD.TwoPi * Math.Sqrt(a * a * a / Mu), o.Period, 1e-6 * o.Period);
            Assert.AreEqual(0.0, o.Inclination, 1e-12);

            OrbitPreview retro = ManeuverPlanning.AfterBurn(Mu, 0, rv, vv, new ManeuverNode { Prograde = -dv }, Vector3d.UnitZ);
            Assert.AreEqual(r, retro.ApoapsisRadius, 1e-3, "retrograde: the burn point becomes the apoapsis");
            Assert.Less(retro.PeriapsisRadius, r);
        }

        [Test]
        public void Node_Normal_TiltsThePlane_Radial_RotatesTheApsides()
        {
            double r = 7.0e6, v = Math.Sqrt(Mu / r), dv = 300.0;
            var rv = new Vector3d(r, 0, 0);
            var vv = new Vector3d(0, v, 0);
            OrbitPreview normal = ManeuverPlanning.AfterBurn(Mu, 0, rv, vv, new ManeuverNode { Normal = dv }, Vector3d.UnitZ);
            Assert.AreEqual(Math.Atan2(dv, v), normal.Inclination, 1e-9, "i = atan(Δv_n / v)");
            var node = new ManeuverNode { Normal = dv };
            Vector3d d = node.DeltaVector(rv, vv);
            Assert.AreEqual(dv, d.z, 1e-9, "normal = along r × v (+z here)");

            OrbitPreview radial = ManeuverPlanning.AfterBurn(Mu, 0, rv, vv, new ManeuverNode { Radial = dv }, Vector3d.UnitZ);
            Assert.AreEqual(dv / v, radial.Eccentricity, 1e-9, "e = Δv_r / v_c for a circle");
            Assert.Less(radial.PeriapsisRadius, r);
            Assert.Greater(radial.ApoapsisRadius, r);
            Vector3d dr = new ManeuverNode { Radial = dv }.DeltaVector(rv, vv);
            Assert.AreEqual(dv, dr.x, 1e-9, "radial = outward");

            // Round trip: components → vector → components.
            var mixed = new ManeuverNode { Time = 5.0, Prograde = 123.0, Normal = -45.0, Radial = 67.0 };
            ManeuverNode back = ManeuverPlanning.NodeFromVector(5.0, rv, vv, mixed.DeltaVector(rv, vv));
            Assert.AreEqual(123.0, back.Prograde, 1e-9);
            Assert.AreEqual(-45.0, back.Normal, 1e-9);
            Assert.AreEqual(67.0, back.Radial, 1e-9);
        }

        [Test]
        public void BurnEstimate_FollowsTsiolkovsky_AcrossStages()
        {
            var rocket = new RocketState(new RocketConfiguration());
            RocketConfiguration c = rocket.Configuration;
            double m0 = rocket.Mass;
            BurnEstimate small = ManeuverPlanning.Estimate(rocket, 100.0);
            Assert.IsTrue(small.Sufficient);
            Assert.AreEqual(1, small.StagesUsed);
            double expectedProp = m0 - m0 / Math.Exp(100.0 / (c.stages[0].vacuumIsp * 9.80665));
            Assert.AreEqual(expectedProp, small.Propellant, 1e-6 * expectedProp);
            Assert.AreEqual(expectedProp / RocketEquations.MassFlow(c.stages[0].vacuumThrust, c.stages[0].vacuumIsp), small.Duration, 1e-6);
            Assert.AreEqual(RocketEquations.BurnTimeForDeltaV(100.0, c.stages[0].vacuumIsp, m0, c.stages[0].vacuumThrust), small.Duration, 1e-6);

            double all = rocket.DeltaVRemaining();
            BurnEstimate big = ManeuverPlanning.Estimate(rocket, all - 10.0);
            Assert.IsTrue(big.Sufficient);
            Assert.AreEqual(2, big.StagesUsed);
            BurnEstimate tooBig = ManeuverPlanning.Estimate(rocket, all + 10.0);
            Assert.IsFalse(tooBig.Sufficient);
        }

    }
}
