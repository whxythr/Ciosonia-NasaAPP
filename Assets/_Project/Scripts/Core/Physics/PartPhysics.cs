using System;

namespace SpaceSim.Physics
{
    /// <summary>Electric budget of a vehicle at one moment [W, J].</summary>
    public readonly struct PowerBalance
    {
        public PowerBalance(double generation, double consumption, double engineDemand, double batteryPower, double batteryEnergy)
        {
            Generation = Math.Max(0.0, generation);
            Consumption = Math.Max(0.0, consumption);
            EngineDemand = Math.Max(0.0, engineDemand);
            BatteryPower = Math.Max(0.0, batteryPower);
            BatteryEnergy = Math.Max(0.0, batteryEnergy);
        }

        /// <summary>Output of the generators (panels, RTGs, reactors) [W].</summary>
        public double Generation { get; }
        /// <summary>Draw of everything but the thrusters [W].</summary>
        public double Consumption { get; }
        /// <summary>Thrusters at full thrust [W].</summary>
        public double EngineDemand { get; }
        /// <summary>Greatest battery discharge [W].</summary>
        public double BatteryPower { get; }
        /// <summary>Energy left in the batteries [J].</summary>
        public double BatteryEnergy { get; }

        /// <summary>Generators minus systems: what is left without touching the batteries (negative = the systems run on batteries) [W].</summary>
        public double NetGeneration => Generation - Consumption;
        /// <summary>Power the thrusters can have right now [W].</summary>
        public double AvailableForEngines => Math.Max(0.0, NetGeneration + (BatteryEnergy > 0.0 ? BatteryPower : 0.0));
        /// <summary>Share of the full thrust that power sustains: 1 = enough (also when no thruster needs any), 0 = none.</summary>
        public double EngineFraction => EngineDemand > 0.0 ? Math.Min(1.0, AvailableForEngines / EngineDemand) : 1.0;
        /// <summary>Generators cover the systems without the batteries.</summary>
        public bool SystemsCovered => Generation >= Consumption;
    }

    /// <summary>Touchdown limits and verdict from <see cref="PartPhysics.AssessTouchdown"/>.</summary>
    public readonly struct TouchdownCheck
    {
        public TouchdownCheck(LandingResult result, double softLimit, double crashLimit, bool onLegs)
        {
            Result = result;
            SoftLimit = softLimit;
            CrashLimit = crashLimit;
            OnLegs = onLegs;
        }

        public LandingResult Result { get; }
        /// <summary>Highest downward speed of a soft landing [m/s].</summary>
        public double SoftLimit { get; }
        /// <summary>Above this downward speed the vehicle is destroyed [m/s].</summary>
        public double CrashLimit { get; }
        /// <summary>The limits are the legs' (false: the bare hull's).</summary>
        public bool OnLegs { get; }
    }

    /// <summary>
    /// Formulas of the non-propulsive parts (Level A, SI units, no state): parachutes, legs, fins, RCS, antennas, electric power, rovers.
    /// The hangar and the flight call the same functions. Nothing here decides anything for the player.
    /// </summary>
    public static class PartPhysics
    {
        // ---- Parachutes ----------------------------------------------------------------------------------

        /// <summary>
        /// Steady descent speed under a canopy, where drag equals weight: v = √(2·m·g/(ρ·C_d·A)) [m/s].
        /// No air or no canopy: +∞ (nothing limits the fall).
        /// </summary>
        public static double ParachuteDescentSpeed(double mass, double gravity, double density, double dragCoefficient, double area) =>
            ParachuteDescentSpeed(mass, gravity, density, dragCoefficient * area);

        /// <summary>The same with the summed C_d·A of all the canopies [m²].</summary>
        public static double ParachuteDescentSpeed(double mass, double gravity, double density, double dragArea)
        {
            if (mass <= 0.0 || gravity <= 0.0) return 0.0;
            return density > 0.0 && dragArea > 0.0 ? Math.Sqrt(2.0 * mass * gravity / (density * dragArea)) : double.PositiveInfinity;
        }

        /// <summary>Time for a canopy to inflate fully once it meets enough air [s].</summary>
        public const double ParachuteInflationTime = 3.0;
        /// <summary>Below this dynamic pressure a canopy does not fill: it trails limp [Pa].</summary>
        public const double ParachuteMinInflationPressure = 5.0;

        /// <summary>
        /// Whether a canopy is torn at dynamic pressure q [Pa] and air speed v [m/s]: the load exceeds its strength
        /// (q &gt; q_max) or it flutters to pieces at a speed it was not built for (v &gt; v_max, thin air at high speed).
        /// A limit ≤ 0 is not checked.
        /// </summary>
        public static bool ParachuteTears(double dynamicPressure, double airSpeed, double maxDynamicPressure, double maxSpeed) =>
            (maxDynamicPressure > 0.0 && dynamicPressure > maxDynamicPressure) || (maxSpeed > 0.0 && airSpeed > maxSpeed);

        /// <summary>Inflation 0..1 after dt more seconds: grows linearly over <see cref="ParachuteInflationTime"/> while q ≥ the filling pressure.</summary>
        public static double ParachuteInflation(double opening, double dynamicPressure, double dt)
        {
            if (dynamicPressure < ParachuteMinInflationPressure || dt <= 0.0) return Math.Max(0.0, Math.Min(1.0, opening));
            return Math.Min(1.0, Math.Max(0.0, opening) + dt / ParachuteInflationTime);
        }

        /// <summary>Drag force of the canopies [N]: F = ½·ρ·v²·Σ(C_d·A)·opening.</summary>
        public static double ParachuteDrag(double density, double airSpeed, double dragArea, double opening) =>
            0.5 * Math.Max(0.0, density) * airSpeed * airSpeed * Math.Max(0.0, dragArea) * Math.Max(0.0, Math.Min(1.0, opening));

        // ---- Landing legs --------------------------------------------------------------------------------

        /// <summary>A vehicle needs this many legs to stand.</summary>
        public const int MinLegsToStand = 3;
        /// <summary>Soft contact of a bare hull with the ground [m/s].</summary>
        public const double HullTouchdownSpeed = 1.5;
        /// <summary>Crash speed = soft speed × this (the ratio of the limits in <see cref="LandingPhysics"/>).</summary>
        public const double CrashToSoftRatio = LandingPhysics.CrashVertical / LandingPhysics.SoftVertical;

        /// <summary>
        /// Touchdown verdict for a downward speed [m/s, positive = down]. With fewer than <see cref="MinLegsToStand"/> legs
        /// the hull takes the contact. <paramref name="legTouchdownSpeed"/> is the weakest leg's limit.
        /// </summary>
        public static TouchdownCheck AssessTouchdown(double downwardSpeed, int legCount, double legTouchdownSpeed)
        {
            bool onLegs = legCount >= MinLegsToStand && legTouchdownSpeed > 0.0;
            double soft = onLegs ? legTouchdownSpeed : HullTouchdownSpeed;
            double crash = soft * CrashToSoftRatio;
            double down = Math.Max(0.0, downwardSpeed);
            LandingResult result = down > crash ? LandingResult.Crash : down > soft ? LandingResult.Hard : LandingResult.Soft;
            return new TouchdownCheck(result, soft, crash, onLegs);
        }

        // ---- Fins -----------------------------------------------------------------------------------------

        /// <summary>Normal-force slope of a body with a nose, on its cross-section (slender-body theory) [1/rad].</summary>
        public const double BodyNormalForceSlope = 2.0;
        /// <summary>Static margin that counts as stable (centre of pressure one body diameter below the centre of mass).</summary>
        public const double StableMarginCalibers = 1.0;
        const double FinAspectRatio = 2.0;

        /// <summary>
        /// Normal-force slope of a fin set on the body's cross-section A [1/rad]: C_Nα = (S/A)·2π·AR/(2 + √(4 + AR²)),
        /// the low-aspect-ratio planar wing with S the planform area of the set, half of which meets the flow on each axis.
        /// </summary>
        public static double FinNormalForceSlope(double finArea, double bodyArea)
        {
            if (finArea <= 0.0 || bodyArea <= 0.0) return 0.0;
            return 0.5 * finArea / bodyArea * 2.0 * Math.PI * FinAspectRatio / (2.0 + Math.Sqrt(4.0 + FinAspectRatio * FinAspectRatio));
        }

        /// <summary>Height of the centre of pressure above the bottom [m]: the body's and the fins' pressure centres, weighted by their normal force.</summary>
        public static double CenterOfPressure(double bodyPressureCenter, double finHeight, double finArea, double bodyArea)
        {
            double fins = FinNormalForceSlope(finArea, bodyArea);
            return (BodyNormalForceSlope * bodyPressureCenter + fins * finHeight) / (BodyNormalForceSlope + fins);
        }

        /// <summary>
        /// Static margin in body diameters: (x_mass − x_pressure)/D, heights from the bottom. Positive = the air turns the nose back
        /// into the flow; negative = the rocket tumbles unless something steers it.
        /// </summary>
        public static double StaticMarginCalibers(double centerOfMass, double bodyPressureCenter, double finHeight, double finArea, double bodyArea, double bodyDiameter)
        {
            if (bodyDiameter <= 0.0) return 0.0;
            return (centerOfMass - CenterOfPressure(bodyPressureCenter, finHeight, finArea, bodyArea)) / bodyDiameter;
        }

        /// <summary>
        /// Restoring moment of the fins [N·m] at dynamic pressure q [Pa] and angle of attack α [rad] about the centre of mass, for a set
        /// <paramref name="arm"/> metres below it: M = q·A·C_Nα·α·arm.
        /// </summary>
        public static double FinRestoringTorque(double dynamicPressure, double finArea, double bodyArea, double angleOfAttack, double arm) =>
            dynamicPressure * bodyArea * FinNormalForceSlope(finArea, bodyArea) * angleOfAttack * arm;

        // ---- RCS ------------------------------------------------------------------------------------------

        /// <summary>Torque of RCS thrusters firing as couples: τ = F·r [N·m], F the total thrust, r the arm about the centre of mass.</summary>
        public static double RcsTorque(double totalThrust, double leverArm) => Math.Max(0.0, totalThrust) * Math.Abs(leverArm);

        /// <summary>Specific impulse of the RCS thrusters (hypergolic monopropellant class) [s].</summary>
        public const double RcsIsp = 220.0;

        /// <summary>Propellant flow of RCS thrusters firing at <paramref name="thrust"/> [N]: ṁ = F/(I_sp·g₀) [kg/s].</summary>
        public static double RcsMassFlow(double thrust) => thrust > 0.0 ? thrust / (RcsIsp * Units.PhysicalConstants.StandardGravity) : 0.0;

        /// <summary>
        /// Moment of inertia of the stack about a transverse axis through its centre [kg·m²]: a solid cylinder,
        /// I = m·(L²/12 + r²/4).
        /// </summary>
        public static double TransverseInertia(double mass, double length, double radius) =>
            Math.Max(0.0, mass) * (length * length / 12.0 + radius * radius / 4.0);

        /// <summary>Angular acceleration from a torque [rad/s²]: ε = τ/I.</summary>
        public static double AngularAcceleration(double torque, double inertia) => inertia > 0.0 ? Math.Max(0.0, torque) / inertia : 0.0;

        /// <summary>
        /// The attitude rate is the pilot's own steering (engine gimbal, reaction wheels) — unchanged for every vehicle; an extra
        /// control torque adds the rate it builds up in <see cref="ControlRampTime"/>: ω = ω₀ + ε·t [deg/s], at most <see cref="MaxAttitudeRate"/>.
        /// </summary>
        public static double AttitudeRate(double baseRateDegrees, double angularAcceleration)
        {
            double extra = Math.Max(0.0, angularAcceleration) * ControlRampTime * 180.0 / Math.PI;
            return Math.Max(baseRateDegrees, Math.Min(MaxAttitudeRate, baseRateDegrees + extra));
        }

        /// <summary>Time the extra torque has to build the rate when a turn is commanded [s].</summary>
        public const double ControlRampTime = 1.0;
        /// <summary>Upper bound of the commanded rotation rate [deg/s].</summary>
        public const double MaxAttitudeRate = 60.0;

        // ---- Fins in flight -------------------------------------------------------------------------------

        /// <summary>Grid fins do nothing below this dynamic pressure [Pa]: there is too little air on the lattice.</summary>
        public const double GridFinMinDynamicPressure = 1000.0;
        /// <summary>Largest deflection of a grid fin [rad] (≈ 15°).</summary>
        public const double GridFinDeflection = 0.26;

        /// <summary>Control torque of grid fins [N·m]: the fin force at full deflection times the arm, zero below <see cref="GridFinMinDynamicPressure"/>.</summary>
        public static double GridFinControlTorque(double dynamicPressure, double finArea, double bodyArea, double arm) =>
            dynamicPressure < GridFinMinDynamicPressure ? 0.0 : FinRestoringTorque(dynamicPressure, finArea, bodyArea, GridFinDeflection, arm);

        /// <summary>
        /// Weathervane stability of side fins: the restoring moment M = q·A·C_Nα·α·arm turns the nose into the airflow. For a rigid body
        /// with critical damping the angle closes at ω = α·√(q·A·C_Nα·arm/I) [rad/s]. Returns that rate for angle of attack α [rad].
        /// No fins or no air: 0 (the vehicle keeps whatever attitude the pilot holds).
        /// </summary>
        public static double WeathervaneRate(double dynamicPressure, double finArea, double bodyArea, double arm, double inertia, double angleOfAttack)
        {
            if (dynamicPressure <= 0.0 || finArea <= 0.0 || inertia <= 0.0) return 0.0;
            double stiffness = dynamicPressure * bodyArea * FinNormalForceSlope(finArea, bodyArea) * Math.Abs(arm);
            return Math.Abs(angleOfAttack) * Math.Sqrt(stiffness / inertia);
        }

        /// <summary>
        /// Open canopies pull at the top of the stack: their drag D = q·C_dA acting on the arm turns the axis AGAINST the airflow
        /// (nose up under the canopy), like a pendulum. Critically damped rate ω = θ·√(q·C_dA·arm/I) for the angle θ [rad] between
        /// the axis and the direction opposite to the airflow. No canopy or no air: 0.
        /// </summary>
        public static double CanopyRightingRate(double dynamicPressure, double canopyDragArea, double arm, double inertia, double angle)
        {
            if (dynamicPressure <= 0.0 || canopyDragArea <= 0.0 || inertia <= 0.0) return 0.0;
            return Math.Abs(angle) * Math.Sqrt(dynamicPressure * canopyDragArea * Math.Abs(arm) / inertia);
        }

        // ---- Heat shield ---------------------------------------------------------------------------------

        /// <summary>
        /// Stagnation heat flux a bare hull takes without harm [W/m²]: well above a shallow return from low orbit, below a steep or
        /// interplanetary entry (Level A rule of the game).
        /// </summary>
        public const double HullHeatFluxLimit = 1.0e6;
        /// <summary>Equipment integrity lost per second at twice the allowed flux (proportional to the excess).</summary>
        public const double HeatDamageRate = 0.02;

        /// <summary>The flux the vehicle tolerates [W/m²]: its best heat shield, never below the bare hull.</summary>
        public static double AllowedHeatFlux(double shieldLimit) => Math.Max(HullHeatFluxLimit, shieldLimit);

        /// <summary>Integrity lost per second at heat flux q̇ [W/m²] with the tolerated flux q̇_max: k·(q̇/q̇_max − 1) above the limit, 0 below.</summary>
        public static double HeatDamagePerSecond(double heatFlux, double allowedFlux)
        {
            if (allowedFlux <= 0.0 || heatFlux <= allowedFlux) return 0.0;
            return HeatDamageRate * (heatFlux / allowedFlux - 1.0);
        }

        // ---- Communication ---------------------------------------------------------------------------------

        /// <summary>
        /// Inverse-square law: the received power falls as 1/d², so the reach grows as √power:
        /// R = R₀·√(P/P₀) [m] for an antenna of range R₀ at P₀ [W].
        /// </summary>
        public static double CommRange(double nominalRange, double nominalPower, double power)
        {
            if (nominalRange <= 0.0 || nominalPower <= 0.0 || power <= 0.0) return 0.0;
            return nominalRange * Math.Sqrt(power / nominalPower);
        }

        /// <summary>Received signal relative to the weakest usable one: (R/d)², capped at 1. Below 1 the link is out of range.</summary>
        public static double SignalStrength(double range, double distance)
        {
            if (range <= 0.0) return 0.0;
            if (distance <= 0.0) return 1.0;
            double ratio = range / distance;
            return Math.Min(1.0, ratio * ratio);
        }

        /// <summary>Reach of several antennas working together [m]: received powers add, so R = √(ΣRᵢ²).</summary>
        public static double CombineRange(double rangeA, double rangeB) => Math.Sqrt(rangeA * rangeA + rangeB * rangeB);

        // ---- Electric power --------------------------------------------------------------------------------

        /// <summary>Total balance of generation, systems, thrusters and batteries.</summary>
        public static PowerBalance ElectricBalance(double generation, double consumption, double engineDemand, double batteryPower, double batteryEnergy) =>
            new PowerBalance(generation, consumption, engineDemand, batteryPower, batteryEnergy);

        /// <summary>Solar flux at a distance relative to a reference one: (d₀/d)² (1 at the reference distance).</summary>
        public static double SolarFluxRatio(double distance, double referenceDistance) =>
            distance > 0.0 && referenceDistance > 0.0 ? referenceDistance * referenceDistance / (distance * distance) : 1.0;

        // ---- Rover ------------------------------------------------------------------------------------------

        /// <summary>Fewest wheels a rover can be built on.</summary>
        public const int MinRoverWheels = 4;

        /// <summary>The wheels carry the rover's weight m·g at the local gravity (the same wheels may be enough on a small world and not on a big one).</summary>
        public static bool WheelsCarry(double totalWheelLoad, double mass, double gravity) => mass * gravity <= totalWheelLoad;
    }
}
