using System;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Vehicles;

namespace SpaceSim.Rover
{
    /// <summary>What the rover is made of and has to move (SI).</summary>
    public readonly struct RoverSpec
    {
        public RoverSpec(double mass, int wheelCount, double wheelLoad, double wheelMaxSpeed, double drivePower)
        {
            Mass = Math.Max(1e-6, mass);
            WheelCount = Math.Max(0, wheelCount);
            WheelLoad = Math.Max(0.0, wheelLoad);
            WheelMaxSpeed = Math.Max(0.0, wheelMaxSpeed);
            DrivePower = Math.Max(0.0, drivePower);
        }

        /// <summary>Everything the wheels carry [kg] (the whole vehicle still standing on them).</summary>
        public double Mass { get; }
        public int WheelCount { get; }
        /// <summary>Weight all wheels carry together [N].</summary>
        public double WheelLoad { get; }
        /// <summary>Speed of the slowest wheel [m/s].</summary>
        public double WheelMaxSpeed { get; }
        /// <summary>Electric power of the drive motors [W].</summary>
        public double DrivePower { get; }

        /// <summary>The rover of a compiled design carrying <paramref name="mass"/> [kg]; the drive power is the drive module's rated draw.</summary>
        public static RoverSpec From(VehicleCapabilities caps, double mass)
        {
            ModuleDefinition drive = ModuleCatalog.Find(ModuleCatalog.RoverDrive);
            double power = caps != null && caps.HasRoverDrive && drive != null ? drive.powerDraw : 0.0;
            return caps == null ? new RoverSpec(mass, 0, 0.0, 0.0, 0.0) : new RoverSpec(mass, caps.WheelCount, caps.WheelLoad, caps.WheelMaxSpeed, power);
        }
    }

    public enum RoverDriveMode
    {
        Stopped,
        Driving,
        /// <summary>The motors give all their power: the slope (or the soil) sets the speed.</summary>
        PowerLimited,
        /// <summary>The wheels spin or skid: the ground cannot take the force asked of it.</summary>
        Slipping,
        /// <summary>Too steep to hold: the rover slides back down even with the wheels locked.</summary>
        SlidingBack,
        /// <summary>The wheels cannot carry the weight: nothing moves.</summary>
        Overloaded
    }

    /// <summary>The rover's state along its heading (planet-fixed, SI).</summary>
    public sealed class RoverMotion
    {
        /// <summary>Speed along the heading [m/s] (negative = backwards).</summary>
        public double Speed;
        /// <summary>Heading [deg]: 0 = north, 90 = east.</summary>
        public double HeadingDegrees;
        /// <summary>Electric energy the drive used [J].</summary>
        public double EnergyUsed;
        /// <summary>Distance driven [m].</summary>
        public double Distance;
    }

    /// <summary>The result of one step: what the drive drew and why the rover moved the way it did.</summary>
    public readonly struct RoverStep
    {
        public RoverStep(double power, double wheelForce, RoverDriveMode mode, double maxSpeed)
        {
            Power = power;
            WheelForce = wheelForce;
            Mode = mode;
            MaxSpeed = maxSpeed;
        }

        /// <summary>Electric power of the drive during the step [W].</summary>
        public double Power { get; }
        /// <summary>Force of the wheels on the ground along the heading [N].</summary>
        public double WheelForce { get; }
        public RoverDriveMode Mode { get; }
        /// <summary>Steady top speed on this grade in the driving direction [m/s].</summary>
        public double MaxSpeed { get; }
    }

    /// <summary>
    /// Wheeled driving on a slope (Level A, SI, no state of its own). Along the heading the rover feels the weight component
    /// m·g·sin θ, rolling resistance C_rr·m·g·cos θ and the wheel force, which the ground limits to μ·m·g·cos θ (traction)
    /// and the motors to η·P/v (power). So the top speed falls with the grade and with the planet's gravity, the climb ends where
    /// tan θ = μ − C_rr (the wheels spin and the rover stops or slips back), and beyond tan θ = μ even locked wheels slide.
    /// Steering is kinematic: yaw rate = v/R. No autopilot: the player's throttle and steering are the only inputs.
    /// </summary>
    public static class RoverKinematics
    {
        /// <summary>Wheel–regolith friction coefficient (loose, dry soil).</summary>
        public const double Friction = 0.6;
        /// <summary>Rolling resistance of a wheel sinking into loose soil.</summary>
        public const double RollingResistance = 0.05;
        /// <summary>Battery-to-wheel efficiency of the electric drive.</summary>
        public const double DriveEfficiency = 0.75;
        /// <summary>Tightest turn at full steering [m].</summary>
        public const double MinTurnRadius = 3.0;
        /// <summary>How quickly the driver's command is followed [s].</summary>
        public const double ResponseTime = 0.6;
        /// <summary>Below this speed the motors are limited by torque, not by power [m/s].</summary>
        public const double TorqueLimitSpeed = 0.2;
        /// <summary>Steering angle of the wheels at full lock (display) [deg].</summary>
        public const double MaxSteerDegrees = 25.0;

        /// <summary>Steepest grade the wheels can climb: tan θ = μ − C_rr [deg].</summary>
        public static double ClimbLimitDegrees => Math.Atan(Friction - RollingResistance) * MathD.RadToDeg;

        /// <summary>Steepest grade on which locked wheels still hold: tan θ = μ [deg].</summary>
        public static double HoldLimitDegrees => Math.Atan(Friction) * MathD.RadToDeg;

        /// <summary>Force needed to keep moving up a grade θ [rad, + uphill]: m·g·(sin θ + C_rr·cos θ) [N].</summary>
        public static double ClimbResistance(double mass, double gravity, double grade) =>
            mass * gravity * (Math.Sin(grade) + RollingResistance * Math.Cos(grade));

        /// <summary>Largest wheel force the ground can take: μ·m·g·cos θ [N].</summary>
        public static double TractionLimit(double mass, double gravity, double grade) => Friction * mass * gravity * Math.Max(0.0, Math.Cos(grade));

        /// <summary>
        /// Share of the drive's power the electric system can give [0..1]: all of it while the generators cover the systems or the
        /// batteries hold charge; otherwise only the generators' share of the systems' draw.
        /// </summary>
        public static double SupplyFraction(PowerBalance electrics)
        {
            if (electrics.SystemsCovered || electrics.BatteryEnergy > 0.0) return 1.0;
            return electrics.Consumption > 0.0 ? MathD.Clamp(electrics.Generation / electrics.Consumption, 0.0, 1.0) : 1.0;
        }

        /// <summary>
        /// Steady top speed up a grade θ [rad, + uphill] [m/s]: the wheels' rating, the power η·P/F and nothing at all where the
        /// traction is not enough or the wheels cannot carry the weight.
        /// </summary>
        public static double MaxSpeed(RoverSpec spec, double gravity, double grade, double supply = 1.0)
        {
            if (spec.WheelCount < PartPhysics.MinRoverWheels || !PartPhysics.WheelsCarry(spec.WheelLoad, spec.Mass, gravity)) return 0.0;
            double need = ClimbResistance(spec.Mass, gravity, grade);
            if (need > TractionLimit(spec.Mass, gravity, grade)) return 0.0;
            double power = DriveEfficiency * spec.DrivePower * MathD.Clamp(supply, 0.0, 1.0);
            if (need <= 0.0) return spec.WheelMaxSpeed;
            return Math.Min(spec.WheelMaxSpeed, power / need);
        }

        /// <summary>
        /// Advances the rover by <paramref name="dt"/> [s]. <paramref name="grade"/> is the slope along the heading [rad, + uphill],
        /// <paramref name="throttle"/> and <paramref name="steer"/> ∈ [−1, 1] (steer + = right), <paramref name="supply"/> from
        /// <see cref="SupplyFraction"/>.
        /// </summary>
        public static RoverStep Step(RoverMotion motion, RoverSpec spec, double gravity, double grade, double throttle, double steer, double supply, double dt)
        {
            if (motion == null) throw new ArgumentNullException(nameof(motion));
            double m = spec.Mass, g = Math.Max(0.0, gravity);
            double sin = Math.Sin(grade), cos = Math.Max(0.0, Math.Cos(grade));
            throttle = MathD.Clamp(throttle, -1.0, 1.0);
            double topForward = MaxSpeed(spec, g, throttle >= 0.0 ? grade : -grade, supply);
            if (dt <= 0.0) return new RoverStep(0.0, 0.0, Math.Abs(motion.Speed) < 0.01 ? RoverDriveMode.Stopped : RoverDriveMode.Driving, topForward);

            bool carries = spec.WheelCount >= PartPhysics.MinRoverWheels && PartPhysics.WheelsCarry(spec.WheelLoad, m, g);
            double power = carries ? DriveEfficiency * spec.DrivePower * MathD.Clamp(supply, 0.0, 1.0) : 0.0;
            double gravityForce = -m * g * sin;                 // along the heading
            double rolling = RollingResistance * m * g * cos;   // magnitude, against the motion
            double traction = Friction * m * g * cos;
            double v = motion.Speed;

            double target = throttle * spec.WheelMaxSpeed;
            double direction = Math.Abs(v) > 1e-3 ? Math.Sign(v) : Math.Sign(target);
            double required = m * (target - v) / ResponseTime - gravityForce + rolling * direction;
            double force = required;
            bool powerLimited = false;
            double vRef = Math.Max(Math.Abs(v), TorqueLimitSpeed);
            // Pushing in the direction of motion (or starting off) costs electric power; braking needs only the ground's grip.
            bool motoring = force * (Math.Abs(v) > 1e-3 ? v : target) > 0.0;
            if (motoring && Math.Abs(force) > power / vRef)
            {
                force = Math.Sign(force) * power / vRef;
                powerLimited = true;
            }
            // The ground takes no more than μ·N: beyond it the wheels spin (driving) or skid (braking).
            bool slipping = Math.Abs(force) > traction + 1e-9;
            force = MathD.Clamp(force, -traction, traction);
            if (!carries)
            {
                // The wheels buckle: the vehicle rests on its frame and stays where it is.
                motion.Speed = 0.0;
                return new RoverStep(0.0, 0.0, RoverDriveMode.Overloaded, 0.0);
            }

            double drive = force + gravityForce;
            double vNew;
            if (Math.Abs(v) <= 1e-3 && Math.Abs(drive) <= rolling) vNew = 0.0; // static: rolling resistance holds it
            else
            {
                double sign = Math.Abs(v) > 1e-3 ? Math.Sign(v) : Math.Sign(drive);
                vNew = v + (drive - rolling * sign) / m * dt;
                if (Math.Abs(v) > 1e-3 && Math.Sign(vNew) != Math.Sign(v) && Math.Abs(drive) <= rolling) vNew = 0.0;
            }

            double vAvg = 0.5 * (v + vNew);
            double mechanical = Math.Max(0.0, force * vAvg);
            double electric = Math.Min(mechanical / DriveEfficiency, spec.DrivePower * MathD.Clamp(supply, 0.0, 1.0));
            motion.Speed = vNew;
            motion.Distance += Math.Abs(vAvg) * dt;
            motion.EnergyUsed += electric * dt;
            motion.HeadingDegrees = Repeat360(motion.HeadingDegrees + vAvg / MinTurnRadius * MathD.Clamp(steer, -1.0, 1.0) * dt * MathD.RadToDeg);

            RoverDriveMode mode;
            bool downhill = vNew * sin < 0.0; // moving down the grade
            bool unwanted = throttle == 0.0 ? Math.Abs(vNew) > 0.01 : vNew * throttle < 0.0 && Math.Abs(vNew) > 0.01;
            if (slipping && downhill && unwanted) mode = RoverDriveMode.SlidingBack;
            else if (slipping) mode = RoverDriveMode.Slipping;
            else if (powerLimited) mode = RoverDriveMode.PowerLimited;
            else mode = Math.Abs(vNew) < 0.01 ? RoverDriveMode.Stopped : RoverDriveMode.Driving;
            return new RoverStep(electric, force, mode, topForward);
        }

        /// <summary>A short explanation of the mode with the numbers that decide it (for the HUD).</summary>
        public static string Describe(RoverDriveMode mode, RoverSpec spec, double gravity)
        {
            switch (mode)
            {
                case RoverDriveMode.PowerLimited: return Loc.T("предел мощности: v = ηP/F");
                case RoverDriveMode.Slipping: return Loc.T("буксует: нужно больше μ·m·g·cos θ");
                case RoverDriveMode.SlidingBack: return Loc.T("скатывается: склон круче ") + Math.Round(HoldLimitDegrees) + Loc.T("° (tg θ > μ)");
                case RoverDriveMode.Overloaded:
                    return Loc.T("колёса не держат вес: m·g = ") + Math.Round(spec.Mass * gravity) + Loc.T(" Н > ") + Math.Round(spec.WheelLoad) + Loc.T(" Н");
                case RoverDriveMode.Driving: return Loc.T("едет");
                default: return Loc.T("стоит");
            }
        }

        static double Repeat360(double deg)
        {
            deg %= 360.0;
            return deg < 0.0 ? deg + 360.0 : deg;
        }
    }

    /// <summary>The ground under the rover: slope along a heading and the surface normal (central differences of the heights).</summary>
    public static class RoverTerrain
    {
        /// <summary>Grade along the heading at a planet-fixed point [rad, + uphill].</summary>
        public static double GradeAlong(ISurfaceHeightProvider surface, double radius, Vector3d fixedDirection, double headingDegrees, double step = 1.5)
        {
            Vector3d up = fixedDirection.Normalized;
            Vector3d fwd = Heading(up, headingDegrees);
            double a = step / radius;
            double ahead = surface.SurfaceHeight((up + fwd * a).Normalized), behind = surface.SurfaceHeight((up - fwd * a).Normalized);
            return Math.Atan((ahead - behind) / (2.0 * step));
        }

        /// <summary>Surface normal at a planet-fixed point (unit, planet-fixed).</summary>
        public static Vector3d Normal(ISurfaceHeightProvider surface, double radius, Vector3d fixedDirection, double step = 1.5)
        {
            Vector3d up = fixedDirection.Normalized;
            PlanetRotation.LocalBasis(up, out Vector3d east, out Vector3d north);
            double a = step / radius;
            double hE = surface.SurfaceHeight((up + east * a).Normalized), hW = surface.SurfaceHeight((up - east * a).Normalized);
            double hN = surface.SurfaceHeight((up + north * a).Normalized), hS = surface.SurfaceHeight((up - north * a).Normalized);
            double gx = (hE - hW) / (2.0 * step), gy = (hN - hS) / (2.0 * step);
            return (up - east * gx - north * gy).Normalized;
        }

        /// <summary>Unit horizontal direction of a heading (0 = north, 90 = east) at a planet-fixed up.</summary>
        public static Vector3d Heading(Vector3d up, double headingDegrees)
        {
            PlanetRotation.LocalBasis(up, out Vector3d east, out Vector3d north);
            double h = headingDegrees * MathD.DegToRad;
            return north * Math.Cos(h) + east * Math.Sin(h);
        }

        /// <summary>The planet-fixed point after moving <paramref name="distance"/> metres along a heading over the reference sphere.</summary>
        public static Vector3d Advance(Vector3d fixedDirection, double headingDegrees, double distance, double radius)
        {
            Vector3d up = fixedDirection.Normalized;
            return (up + Heading(up, headingDegrees) * (distance / radius)).Normalized;
        }
    }
}
