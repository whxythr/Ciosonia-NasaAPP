using System;
using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Tests
{
    public class OrbitApsidesTests
    {
        const double Mu = 3.986004418e14;

        /// <summary>State on an ellipse with the periapsis on +X, at true anomaly θ (orbit in the XY plane).</summary>
        static void State(double rp, double ra, double theta, out Vector3d r, out Vector3d v)
        {
            double e = (ra - rp) / (ra + rp);
            double p = rp * (1.0 + e);
            double d = p / (1.0 + e * Math.Cos(theta));
            r = new Vector3d(d * Math.Cos(theta), d * Math.Sin(theta), 0.0);
            double k = Math.Sqrt(Mu / p);
            v = new Vector3d(-k * Math.Sin(theta), k * (e + Math.Cos(theta)), 0.0);
        }

        [Test]
        public void Ellipse_RadiiAndPointsMatchTheConstruction()
        {
            const double rp = 6.7e6, ra = 2.0e7;
            State(rp, ra, 1.0, out Vector3d r, out Vector3d v);
            OrbitApsides a = OrbitApsides.FromState(Mu, r, v);

            Assert.IsTrue(a.IsBound);
            Assert.IsTrue(a.HasLine);
            Assert.AreEqual(rp, a.PeriapsisRadius, 1e-3);
            Assert.AreEqual(ra, a.ApoapsisRadius, 1e-2);
            Assert.AreEqual(rp, a.PeriapsisPosition.x, 1e-2);
            Assert.AreEqual(0.0, a.PeriapsisPosition.y, 1e-2);
            Assert.AreEqual(-ra, a.ApoapsisPosition.x, 1e-1);
            double sma = 0.5 * (rp + ra);
            Assert.AreEqual(2.0 * Math.PI * Math.Sqrt(sma * sma * sma / Mu), a.Period, 1e-6);
        }

        [Test]
        public void Ellipse_TimesAgreeWithPropagation()
        {
            State(6.7e6, 2.0e7, 1.0, out Vector3d r, out Vector3d v);
            OrbitApsides a = OrbitApsides.FromState(Mu, r, v);

            ConicPropagator.Propagate(Mu, r, v, a.TimeToPeriapsis, out Vector3d atPeriapsis, out _);
            Assert.AreEqual(a.PeriapsisRadius, atPeriapsis.Magnitude, 1.0);
            ConicPropagator.Propagate(Mu, r, v, a.TimeToApoapsis, out Vector3d atApoapsis, out _);
            Assert.AreEqual(a.ApoapsisRadius, atApoapsis.Magnitude, 1.0);
            // Leaving periapsis at θ = 1 rad: the apoapsis comes first.
            Assert.Less(a.TimeToApoapsis, a.TimeToPeriapsis);
        }

        [Test]
        public void Circle_HasNoLineOfApsides()
        {
            const double radius = 7.0e6;
            var r = new Vector3d(radius, 0.0, 0.0);
            var v = new Vector3d(0.0, Math.Sqrt(Mu / radius), 0.0);
            OrbitApsides a = OrbitApsides.FromState(Mu, r, v);

            Assert.IsFalse(a.HasLine);
            Assert.IsTrue(a.IsBound);
            Assert.AreEqual(radius, a.PeriapsisRadius, 1e-3);
            Assert.AreEqual(radius, a.ApoapsisRadius, 1e-3);
            Assert.IsTrue(double.IsNaN(a.TimeToPeriapsis));
        }

        [Test]
        public void Hyperbola_HasNoApoapsis_AndPeriapsisOnlyBeforePassage()
        {
            const double rp = 7.0e6;
            double vp = 1.2 * Math.Sqrt(2.0 * Mu / rp);
            var r0 = new Vector3d(rp, 0.0, 0.0);
            var v0 = new Vector3d(0.0, vp, 0.0);

            ConicPropagator.Propagate(Mu, r0, v0, 1000.0, out Vector3d after, out Vector3d afterV);
            OrbitApsides outbound = OrbitApsides.FromState(Mu, after, afterV);
            Assert.IsFalse(outbound.IsBound);
            Assert.IsTrue(double.IsPositiveInfinity(outbound.ApoapsisRadius));
            Assert.IsTrue(double.IsPositiveInfinity(outbound.TimeToPeriapsis));
            Assert.AreEqual(rp, outbound.PeriapsisRadius, 1.0);

            ConicPropagator.Propagate(Mu, r0, v0, -1000.0, out Vector3d before, out Vector3d beforeV);
            Assert.AreEqual(1000.0, OrbitApsides.FromState(Mu, before, beforeV).TimeToPeriapsis, 1e-3);
        }

        [Test]
        public void InvalidInput_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OrbitApsides.FromState(0.0, Vector3d.UnitX, Vector3d.UnitY));
            Assert.Throws<ArgumentException>(() => OrbitApsides.FromState(Mu, Vector3d.Zero, Vector3d.UnitY));
        }
    }
}
