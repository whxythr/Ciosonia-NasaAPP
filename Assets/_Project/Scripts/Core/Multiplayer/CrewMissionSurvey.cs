using System;
using System.Collections.Generic;
using SpaceSim.Exploration;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Science;

namespace SpaceSim.Multiplayer
{
    /// <summary>
    /// Builds the satellite survey the robot pilot reads before assembling the robot: the target body comes from the mission,
    /// everything measurable (gravity, pressure, temperatures, radiation, acid) from <see cref="HomeSystemCatalog"/>, and the
    /// details of the work place (ground, distance, climb, how many photos or samples) from a seed that every computer of the
    /// room derives the same way. Pure, no Unity.
    /// </summary>
    public static class CrewMissionSurvey
    {
        /// <summary>Where a team mission sends the robot and what it does there.</summary>
        public sealed class Target
        {
            public string BodyId;
            public bool Surface;
            public RobotTask Tasks;
            /// <summary>Orbit work only: distance from the star [m] (0 = the body's own orbit).</summary>
            public double StarDistance;
            public double WorkHours;
            /// <summary>Polar orbit / polar site.</summary>
            public bool Polar;
        }

        const double AU = 1.495978707e11;

        /// <summary>The robot's job in each research mission (ids of <c>MenuCatalog.Scenarios</c>).</summary>
        static readonly Dictionary<string, Target> targets = new Dictionary<string, Target>
        {
            { "weather-watch", new Target { BodyId = HomeSystemCatalog.HomeWorldId, Tasks = RobotTask.Photos, WorkHours = 24.0 } },
            { "relay-ring", new Target { BodyId = HomeSystemCatalog.HomeWorldId, Tasks = RobotTask.Relay | RobotTask.Photos, WorkHours = 36.0 } },
            { "quoris-mapper", new Target { BodyId = HomeSystemCatalog.QuorisId, Tasks = RobotTask.Photos | RobotTask.IceRadar, WorkHours = 48.0, Polar = true } },
            { "teonan-sungrazer", new Target { BodyId = HomeSystemCatalog.TeonanId, Tasks = RobotTask.Photos, StarDistance = 0.1 * AU, WorkHours = 30.0 } },
            { "mora-lander", new Target { BodyId = HomeSystemCatalog.MoraMoonId, Surface = true, Tasks = RobotTask.Samples | RobotTask.Photos } },
        };

        /// <summary>The robot's job for a mission; unknown missions get a surface trip on the homeworld.</summary>
        public static Target TargetOf(string scenarioId) =>
            scenarioId != null && targets.TryGetValue(scenarioId, out Target t) ? t
                : new Target { BodyId = HomeSystemCatalog.HomeWorldId, Surface = true, Tasks = RobotTask.Photos | RobotTask.Samples };

        /// <summary>Stable across computers and .NET versions (string.GetHashCode is not).</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s ?? "") h = (h ^ c) * 16777619;
                return (int)(h & 0x7fffffff);
            }
        }

        public static RobotSurvey Build(string scenarioId, string roomCode) =>
            Build(TargetOf(scenarioId), HomeSystemCatalog.Bodies(), HomeSystemCatalog.Star(), StableHash(scenarioId + "|" + roomCode));

        public static RobotSurvey Build(Target target, List<CelestialBodyPreset> bodies, StarProperties star, int seed)
        {
            CelestialBodyPreset body = bodies.Find(b => b.Id == target.BodyId) ?? bodies[0];
            var rng = new Random(seed);
            PlanetPhysicalProperties p = body.Physical;
            AtmosphereProperties air = p.atmosphere;
            bool hasAir = air != null && air.hasAtmosphere && air.surfacePressure > 0.0;

            var s = new RobotSurvey
            {
                BodyId = body.Id,
                BodyName = body.DisplayName,
                Surface = target.Surface,
                Gravity = p.SurfaceGravity,
                Tasks = target.Tasks,
                Seed = seed,
            };

            double starDistance = target.StarDistance > 0.0 ? target.StarDistance : DistanceFromStar(body, bodies);
            double equilibrium = SurfaceClimate.EquilibriumTemperature(star.surfaceTemperature, star.radius, starDistance, SurfaceClimate.DefaultBondAlbedo);

            if (target.Surface)
            {
                s.Latitude = target.Polar ? 70.0 + rng.NextDouble() * 15.0 : -40.0 + rng.NextDouble() * 80.0;
                double reference = SurfaceClimate.ReferenceFor(air, equilibrium);
                var climate = new SurfaceClimate(p, reference, SurfaceClimate.DefaultBondAlbedo);
                double mean = climate.Temperature(s.Latitude * Math.PI / 180.0, 0.0, SurfaceClimate.DefaultBondAlbedo);
                // Day–night swing: whatever contrast the air does not even out (vacuum ±35 %, Venus-like air almost none).
                double swing = 0.35 * climate.RedistributionWeight * mean;
                s.MinTemperature = Math.Max(SurfaceClimate.MinimumTemperature, mean - swing);
                s.MaxTemperature = mean + swing;
                s.Pressure = hasAir ? air.surfacePressure : 0.0;
                HazardExposure e = HazardModel.Evaluate(body.Hazards, hasAir ? 1.0 : 0.0, 0, 0);
                s.Radiation = e.Radiation;
                s.Acid = e.Acid;
                s.Ground = GroundOf(body, s.MaxTemperature, rng);
                s.DistanceKm = Math.Round(2.0 + rng.NextDouble() * 8.0, 1);
                s.Return = true; // the robot comes back to the rocket with what it found
                s.ClimbM = Math.Round(50.0 + rng.NextDouble() * 350.0);
                s.Photos = 3 + rng.Next(5);
                s.Samples = (target.Tasks & RobotTask.Samples) != 0 ? 2 + rng.Next(4) : 0;
            }
            else
            {
                // In orbit: open space at the body's distance from the star; the radiation above the air reaches the robot.
                double sunSide = equilibrium * 1.3, shadow = Math.Max(SurfaceClimate.MinimumTemperature, equilibrium * 0.45);
                s.MinTemperature = shadow;
                s.MaxTemperature = sunSide;
                HazardExposure e = HazardModel.Evaluate(body.Hazards, 0.0, 0, 0);
                s.Radiation = Math.Max(e.Radiation, target.StarDistance > 0.0 ? 2 : 0); // close to the star: severe
                s.Acid = 0;
                s.Ground = SurfaceKind.None;
                s.WorkHours = target.WorkHours;
                s.Latitude = target.Polar ? 90.0 : 0.0;
                s.Photos = 20 + rng.Next(30);
                s.RadarPasses = (target.Tasks & RobotTask.IceRadar) != 0 ? 3 + rng.Next(4) : 0;
                s.RelaySetups = (target.Tasks & RobotTask.Relay) != 0 ? 1 : 0;
            }
            return s;
        }

        /// <summary>Distance of a body (or of the planet a moon circles) from the star [m].</summary>
        static double DistanceFromStar(CelestialBodyPreset body, List<CelestialBodyPreset> bodies)
        {
            CelestialBodyPreset b = body;
            for (int guard = 0; guard < 4 && b != null && !string.IsNullOrEmpty(b.ParentId); guard++)
                b = bodies.Find(o => o.Id == b.ParentId);
            return b != null && b.Orbit != null && b.Orbit.semiMajorAxis > 0.0 ? b.Orbit.semiMajorAxis : AU;
        }

        /// <summary>Ice where it stays frozen, dust on airless cratered bodies, otherwise mostly rock.</summary>
        static SurfaceKind GroundOf(CelestialBodyPreset body, double maxTemperature, Random rng)
        {
            bool airless = body.Physical.atmosphere == null || !body.Physical.atmosphere.hasAtmosphere;
            double roll = rng.NextDouble();
            if (maxTemperature < 230.0) return roll < 0.7 ? SurfaceKind.Ice : SurfaceKind.Dust;
            if (airless) return roll < 0.65 ? SurfaceKind.Dust : SurfaceKind.Rock;
            return roll < 0.75 ? SurfaceKind.Rock : SurfaceKind.Dust;
        }
    }
}
