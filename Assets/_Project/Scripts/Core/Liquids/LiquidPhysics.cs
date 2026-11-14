using System;
using SpaceSim.Mathematics;
using SpaceSim.Units;

namespace SpaceSim.Liquids
{
    /// <summary>
    /// Closed-form fluid statics/dynamics used by the grid simulation and available to gameplay code (floating objects,
    /// sediment, pipes, capillaries). Allocation-free; SI units.
    /// </summary>
    public static class LiquidPhysics
    {
        /// <summary>Hydrostatic pressure p = p₀ + ρ·g·d at depth d below the free surface [Pa].</summary>
        public static double HydrostaticPressure(double surfacePressure, double density, double gravity, double depth) =>
            surfacePressure + density * gravity * depth;

        /// <summary>Archimedes force on a displaced volume: F = −ρ_fluid·V·g (opposite to gravity) [N].</summary>
        public static Vector3d BuoyantForce(double fluidDensity, double displacedVolume, Vector3d gravity) =>
            gravity * (-fluidDensity * displacedVolume);

        /// <summary>Net weight + buoyancy of a fully immersed body: F = (ρ_body − ρ_fluid)·V·g [N].</summary>
        public static Vector3d NetImmersedForce(double bodyDensity, double fluidDensity, double volume, Vector3d gravity) =>
            gravity * ((bodyDensity - fluidDensity) * volume);

        /// <summary>
        /// Fraction of a freely floating body's volume below the surface, ρ_body/ρ_fluid, capped at 1 (sinks).
        /// Independent of g as long as g ≠ 0; in zero g there is no "floating" and the body stays where it is (returns NaN).
        /// </summary>
        public static double FloatingSubmergedFraction(double bodyDensity, double fluidDensity, double gravity)
        {
            if (!(gravity > 0.0)) return double.NaN;
            if (!(fluidDensity > 0.0)) return 1.0;
            double f = bodyDensity / fluidDensity;
            return f < 1.0 ? f : 1.0;
        }

        /// <summary>Reynolds number Re = ρ·v·L/μ.</summary>
        public static double ReynoldsNumber(double density, double speed, double length, double viscosity) =>
            viscosity > 0.0 ? density * Math.Abs(speed) * length / viscosity : double.PositiveInfinity;

        /// <summary>
        /// Stokes settling velocity of a sphere, v = 2/9·(ρ_p − ρ_f)·g·r²/μ [m/s]. Positive = moves along gravity
        /// (sinks), negative = rises. Valid for Re ≲ 1.
        /// </summary>
        public static double StokesVelocity(double particleDensity, double fluidDensity, double radius, double viscosity, double gravity)
        {
            if (!(viscosity > 0.0)) throw new ArgumentOutOfRangeException(nameof(viscosity));
            return 2.0 / 9.0 * (particleDensity - fluidDensity) * gravity * radius * radius / viscosity;
        }

        /// <summary>
        /// Terminal velocity of a sphere for any Reynolds number: weight − buoyancy = drag with the Schiller–Naumann drag
        /// law C_d = 24/Re·(1 + 0.15·Re^0.687) (Re ≤ 1000) and C_d = 0.44 above. Reduces to Stokes at low Re.
        /// Signed like <see cref="StokesVelocity"/>; 0 in zero gravity.
        /// </summary>
        public static double TerminalVelocity(double particleDensity, double fluidDensity, double radius, double viscosity, double gravity)
        {
            double stokes = StokesVelocity(particleDensity, fluidDensity, radius, viscosity, gravity);
            if (stokes == 0.0 || !(radius > 0.0)) return 0.0;
            double sign = Math.Sign(stokes);
            double force = Math.Abs(particleDensity - fluidDensity) * (4.0 / 3.0) * Math.PI * radius * radius * radius * Math.Abs(gravity);
            double lo = 0.0, hi = Math.Abs(stokes); // Stokes drag is the smallest possible drag → upper bound on speed
            for (int i = 0; i < 80; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (DragForce(mid, radius, fluidDensity, viscosity) > force) hi = mid; else lo = mid;
            }
            return sign * 0.5 * (lo + hi);
        }

        /// <summary>Drag on a sphere of radius r moving at speed v (Schiller–Naumann / Newton regime) [N].</summary>
        public static double DragForce(double speed, double radius, double fluidDensity, double viscosity)
        {
            double re = ReynoldsNumber(fluidDensity, speed, 2.0 * radius, viscosity);
            if (re <= 1000.0) return 6.0 * Math.PI * viscosity * radius * speed * (1.0 + 0.15 * Math.Pow(re, 0.687));
            return 0.5 * 0.44 * fluidDensity * speed * speed * Math.PI * radius * radius;
        }

        /// <summary>Bond number Bo = Δρ·g·L²/σ: gravity vs surface tension (Bo ≪ 1 → capillarity dominates).</summary>
        public static double BondNumber(double densityDifference, double gravity, double length, double surfaceTension) =>
            surfaceTension > 0.0 ? Math.Abs(densityDifference) * gravity * length * length / surfaceTension : double.PositiveInfinity;

        /// <summary>Laplace pressure across a spherical interface Δp = 2σ/r [Pa].</summary>
        public static double LaplacePressure(double surfaceTension, double radius) => 2.0 * surfaceTension / radius;

        /// <summary>
        /// Jurin capillary rise in a tube h = 2σ·cos θ/(ρ·g·r) [m]. Infinite (the liquid wicks along the whole tube) in
        /// zero gravity when it wets (θ &lt; 90°).
        /// </summary>
        public static double CapillaryRise(double surfaceTension, double contactAngleRadians, double density, double gravity, double tubeRadius)
        {
            double num = 2.0 * surfaceTension * Math.Cos(contactAngleRadians);
            double den = density * gravity * tubeRadius;
            if (!(den > 0.0)) return num > 0.0 ? double.PositiveInfinity : (num < 0.0 ? double.NegativeInfinity : 0.0);
            return num / den;
        }

        /// <summary>Hagen–Poiseuille laminar flow through a round pipe Q = π·r⁴·Δp/(8·μ·L) [m³/s].</summary>
        public static double PoiseuilleFlowRate(double pressureDrop, double radius, double length, double viscosity) =>
            Math.PI * radius * radius * radius * radius * pressureDrop / (8.0 * viscosity * length);

        /// <summary>
        /// Steady flow for a pressure drop that overcomes viscous (linear) and inertial (quadratic) losses:
        /// Δp = b·Q + a·Q² → Q = 2Δp / (b + √(b² + 4aΔp)). b = viscous resistance [Pa·s/m³], a = inertial coefficient
        /// [kg/m⁷]. Viscous limit Q = Δp/b (Poiseuille), inertial limit Q = √(Δp/a) (Bernoulli/Torricelli).
        /// </summary>
        public static double CombinedFlowRate(double pressureDrop, double viscousResistance, double inertialCoefficient)
        {
            if (!(pressureDrop > 0.0)) return 0.0;
            double b = viscousResistance > 0.0 ? viscousResistance : 0.0;
            double a = inertialCoefficient > 0.0 ? inertialCoefficient : 0.0;
            if (a == 0.0) return b > 0.0 ? pressureDrop / b : double.PositiveInfinity;
            return 2.0 * pressureDrop / (b + Math.Sqrt(b * b + 4.0 * a * pressureDrop));
        }

        /// <summary>
        /// Flow through a short round pipe/orifice of diameter d and length L: Hagen–Poiseuille resistance
        /// b = 128·μ·L/(π·d⁴) plus exit kinetic energy and entrance loss a = ρ·(1 + K)/(2·A²) [m³/s].
        /// </summary>
        public static double PipeFlowRate(double pressureDrop, double diameter, double length, double viscosity, double density, double entranceLoss = 0.5)
        {
            if (!(diameter > 0.0)) return 0.0;
            double area = 0.25 * Math.PI * diameter * diameter;
            double b = 128.0 * viscosity * Math.Max(length, 0.0) / (Math.PI * diameter * diameter * diameter * diameter);
            double a = density * (1.0 + entranceLoss) / (2.0 * area * area);
            return CombinedFlowRate(pressureDrop, b, a);
        }

        /// <summary>
        /// Clausius–Clapeyron boiling point at pressure p from a reference point (T₀, p₀) and molar ΔH_vap:
        /// 1/T = 1/T₀ − R·ln(p/p₀)/ΔH [K].
        /// </summary>
        public static double ClausiusClapeyronBoilingPoint(double referenceBoilingPoint, double referencePressure, double pressure, double molarEnthalpyOfVaporization)
        {
            if (!(pressure > 0.0)) return 0.0;
            double inv = 1.0 / referenceBoilingPoint - PhysicalConstants.MolarGasConstant * Math.Log(pressure / referencePressure) / molarEnthalpyOfVaporization;
            return inv > 0.0 ? 1.0 / inv : double.PositiveInfinity;
        }
    }
}
