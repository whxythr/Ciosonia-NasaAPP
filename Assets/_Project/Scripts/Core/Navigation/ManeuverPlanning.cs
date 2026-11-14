using System;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Navigation
{
    /// <summary>The orbit right after an impulsive manoeuvre (osculating elements around the same primary).</summary>
    public struct OrbitPreview
    {
        public bool Valid;
        public int Primary;
        public bool Bound;
        public double Eccentricity;
        /// <summary>Distances from the primary's centre [m] (apoapsis +∞ when open).</summary>
        public double PeriapsisRadius, ApoapsisRadius;
        /// <summary>Angle between the orbit plane and the reference plane [rad].</summary>
        public double Inclination;
        /// <summary>[s], +∞ when open.</summary>
        public double Period;
        /// <summary>Speed right after the burn [m/s].</summary>
        public double Speed;
    }

    /// <summary>What a burn costs the real vehicle: duration at full thrust, propellant (Tsiolkovsky, stage by stage) and whether it can be done.</summary>
    public struct BurnEstimate
    {
        public double DeltaV;
        /// <summary>Burn duration at full thrust over the stages it needs [s] (staging pauses not counted).</summary>
        public double Duration;
        /// <summary>Propellant burnt [kg].</summary>
        public double Propellant;
        /// <summary>The stages left have enough Δv.</summary>
        public bool Sufficient;
        /// <summary>Δv available in all remaining stages (vacuum) [m/s].</summary>
        public double Available;
        /// <summary>Stages the burn uses (1 = only the current one).</summary>
        public int StagesUsed;
        /// <summary>Effective Isp of the current stage [s] (0 without one).</summary>
        public double Isp;
        /// <summary>Acceleration at ignition at full thrust [m/s²].</summary>
        public double Acceleration;
    }

    /// <summary>
    /// Manoeuvre-node math: the orbital frame (prograde / normal / radial — the node's X / Y / Z), turning a Δv vector into node
    /// components and back, the orbit after the burn, and the cost of the burn for the actual rocket.
    /// </summary>
    public static class ManeuverPlanning
    {
        /// <summary>Node components (prograde, normal, radial) of a Δv vector for the pre-burn state (r, v).</summary>
        public static ManeuverNode NodeFromVector(double time, Vector3d r, Vector3d v, Vector3d deltaV)
        {
            ManeuverNode.Basis(r, v, out Vector3d p, out Vector3d n, out Vector3d rad);
            return new ManeuverNode
            {
                Time = time, Prograde = Vector3d.Dot(deltaV, p), Normal = Vector3d.Dot(deltaV, n), Radial = Vector3d.Dot(deltaV, rad)
            };
        }

        /// <summary>Osculating orbit after applying the node at the pre-burn state (r, v) around a body with μ.</summary>
        public static OrbitPreview AfterBurn(double mu, int primary, Vector3d r, Vector3d v, ManeuverNode node, Vector3d referenceAxis)
        {
            Vector3d v2 = node != null ? v + node.DeltaVector(r, v) : v;
            return Describe(mu, primary, r, v2, referenceAxis);
        }

        public static OrbitPreview Describe(double mu, int primary, Vector3d r, Vector3d v, Vector3d referenceAxis)
        {
            var o = new OrbitPreview { Primary = primary };
            if (!(mu > 0.0) || !(r.Magnitude > 0.0)) return o;
            Vector3d axis = referenceAxis.SqrMagnitude > 0.0 ? referenceAxis.Normalized : Vector3d.UnitZ;
            OrbitalElements el = OrbitalMechanics.ComputeElements(mu, r, v, axis);
            o.Valid = true;
            o.Bound = el.IsBound && el.Eccentricity < 1.0;
            o.Eccentricity = el.Eccentricity;
            o.PeriapsisRadius = el.PeriapsisRadius;
            o.ApoapsisRadius = o.Bound ? el.ApoapsisRadius : double.PositiveInfinity;
            o.Inclination = el.Inclination;
            o.Period = o.Bound ? el.Period : double.PositiveInfinity;
            o.Speed = v.Magnitude;
            return o;
        }

        /// <summary>
        /// Cost of a burn of <paramref name="deltaV"/> for the rocket as it is now: stage by stage with the vacuum Isp (Tsiolkovsky),
        /// full thrust of each stage (electric stages at the thrust their power allows now).
        /// </summary>
        public static BurnEstimate Estimate(RocketState rocket, double deltaV)
        {
            var b = new BurnEstimate { DeltaV = Math.Max(0.0, deltaV) };
            if (rocket == null || !rocket.HasStages)
            {
                b.Sufficient = b.DeltaV <= 0.0;
                return b;
            }
            RocketConfiguration cfg = rocket.Configuration;
            b.Available = rocket.DeltaVRemaining();
            b.Isp = rocket.CurrentStage.vacuumIsp;
            double massNow = rocket.Mass;
            double thrust0 = RocketEquations.ThrustFromMassFlow(rocket.FullThrottleMassFlow, rocket.CurrentStage.vacuumIsp);
            b.Acceleration = massNow > 0.0 ? thrust0 / massNow : 0.0;

            double need = b.DeltaV;
            for (int i = rocket.StageIndex; i < rocket.StageCount && need > 1e-9; i++)
            {
                RocketStage s = cfg.stages[i];
                if (s.vacuumIsp <= 0.0 || s.vacuumThrust <= 0.0) continue;
                double above = cfg.payloadMass;
                for (int j = i + 1; j < rocket.StageCount; j++) above += cfg.stages[j].dryMass + rocket.Propellant[j];
                double m0 = above + s.dryMass + rocket.Propellant[i];
                double prop = rocket.Propellant[i];
                if (prop <= 0.0) continue;
                double stageDv = RocketEquations.DeltaV(s.vacuumIsp, m0, m0 - prop);
                double use = Math.Min(need, stageDv);
                double m1 = m0 / RocketEquations.MassRatio(use, s.vacuumIsp);
                double burnt = Math.Min(prop, m0 - m1);
                double flow = i == rocket.StageIndex ? rocket.FullThrottleMassFlow : RocketEquations.MassFlow(s.vacuumThrust, s.vacuumIsp);
                b.Propellant += burnt;
                b.Duration += flow > 0.0 ? burnt / flow : double.PositiveInfinity;
                b.StagesUsed++;
                need -= use;
            }
            b.Sufficient = need <= Math.Max(1e-6, 1e-6 * b.DeltaV) && b.DeltaV <= b.Available + 1e-6;
            return b;
        }
    }
}
