using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Liquids
{
    /// <summary>
    /// Ambient conditions a liquid container experiences. Vectors are in the container frame: +Y is the container's
    /// "up" (the open top), X is its width, Z its depth. An upright container on a planet has gravity (0, −g, 0);
    /// a tilted or upside-down container has the gravity vector rotated accordingly; zero vector = free fall.
    /// </summary>
    public sealed class LiquidEnvironment
    {
        public LiquidEnvironment(Vector3d gravity, double ambientPressure, double ambientTemperature, double ambientGasDensity, string name = "")
        {
            if (!gravity.IsFinite) throw new ArgumentException("Gravity must be finite.", nameof(gravity));
            if (ambientPressure < 0.0 || double.IsNaN(ambientPressure)) throw new ArgumentOutOfRangeException(nameof(ambientPressure));
            if (!(ambientTemperature > 0.0)) throw new ArgumentOutOfRangeException(nameof(ambientTemperature));
            if (ambientGasDensity < 0.0 || double.IsNaN(ambientGasDensity)) throw new ArgumentOutOfRangeException(nameof(ambientGasDensity));
            Gravity = gravity;
            AmbientPressure = ambientPressure;
            AmbientTemperature = ambientTemperature;
            AmbientGasDensity = ambientGasDensity;
            Name = name ?? "";
        }

        /// <summary>Gravitational acceleration in the container frame [m/s²].</summary>
        public Vector3d Gravity { get; }
        /// <summary>Absolute pressure of the surrounding gas at the free surface [Pa] (0 in vacuum).</summary>
        public double AmbientPressure { get; }
        /// <summary>Temperature of the surroundings (gas, or the radiative/ground temperature in vacuum) [K].</summary>
        public double AmbientTemperature { get; }
        /// <summary>Density of the surrounding gas [kg/m³] (drives convective heat exchange; 0 in vacuum).</summary>
        public double AmbientGasDensity { get; }
        public string Name { get; }

        public double GravityMagnitude => Gravity.Magnitude;
        public bool HasAtmosphere => AmbientPressure > 0.0 && AmbientGasDensity > 0.0;

        /// <summary>Upright container under gravity g [m/s²].</summary>
        public static LiquidEnvironment Create(double gravity, double ambientPressure, double ambientTemperature, double ambientGasDensity, string name = "") =>
            new LiquidEnvironment(new Vector3d(0.0, -gravity, 0.0), ambientPressure, ambientTemperature, ambientGasDensity, name);

        /// <summary>Free fall / orbit: no effective gravity.</summary>
        public static LiquidEnvironment ZeroGravity(double ambientPressure, double ambientTemperature, double ambientGasDensity, string name = "zero-g") =>
            new LiquidEnvironment(Vector3d.Zero, ambientPressure, ambientTemperature, ambientGasDensity, name);

        /// <summary>Sea-level Earth air: g = 9.80665, 1 atm, 15 °C, ρ = 1.225.</summary>
        public static LiquidEnvironment StandardEarth() => Create(9.80665, 101325.0, 288.15, 1.225, "Earth (standard)");

        /// <summary>
        /// Conditions at <paramref name="altitude"/> above a planet's reference radius, from the planet's physical data:
        /// g = μ/(R + h)², pressure/temperature/density from <see cref="AtmosphericModel"/>. On airless bodies pressure and
        /// gas density are 0 and the temperature is the planet's stated surface temperature (or
        /// <paramref name="temperatureOverride"/> for a local day/night value).
        /// </summary>
        public static LiquidEnvironment FromPlanet(PlanetPhysicalProperties planet, double altitude = 0.0, double? temperatureOverride = null, string name = "")
        {
            if (planet == null) throw new ArgumentNullException(nameof(planet));
            double r = planet.meanRadius + altitude;
            if (!(r > 0.0)) throw new ArgumentOutOfRangeException(nameof(altitude));
            double g = planet.GravitationalParameter / (r * r);

            AtmosphereProperties atm = planet.atmosphere;
            IAtmosphereProfile profile = AtmosphericModel.Create(atm);
            double pressure = Math.Max(0.0, profile.Pressure(altitude));
            double density = Math.Max(0.0, profile.Density(altitude));
            double temperature = profile.Temperature(altitude);
            if (!(temperature > 0.0)) temperature = atm != null && atm.surfaceTemperature > 0.0 ? atm.surfaceTemperature : 2.725;
            if (temperatureOverride.HasValue) temperature = temperatureOverride.Value;
            return new LiquidEnvironment(new Vector3d(0.0, -g, 0.0), pressure, temperature, density, name);
        }

        public LiquidEnvironment WithGravity(Vector3d gravity) =>
            new LiquidEnvironment(gravity, AmbientPressure, AmbientTemperature, AmbientGasDensity, Name);

        public LiquidEnvironment WithTemperature(double temperature) =>
            new LiquidEnvironment(Gravity, AmbientPressure, temperature, AmbientGasDensity, Name);

        public LiquidEnvironment WithPressure(double pressure, double gasDensity) =>
            new LiquidEnvironment(Gravity, pressure, AmbientTemperature, gasDensity, Name);

        public override string ToString() =>
            $"{Name}: g = {GravityMagnitude:G4} m/s², p = {AmbientPressure:G4} Pa, T = {AmbientTemperature:G4} K";
    }
}
