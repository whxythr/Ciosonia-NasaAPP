using System;
using SpaceSim.Economy;
using SpaceSim.Exploration;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using SpaceSim.Vehicles;

namespace SpaceSim.Rover
{
    /// <summary>A core the drill can take under the vehicle: the sample (same record as a surface one) and the depth it comes from.</summary>
    public sealed class DrillCore
    {
        public SampleSite Site;
        /// <summary>Depth of the hole [m] (the deepest drill on board, no deeper).</summary>
        public double Depth;
        /// <summary>Loose soil above the bedrock here [m].</summary>
        public double SoilDepth;
        /// <summary>Drilling energy at the bit [J].</summary>
        public double Energy;
    }

    /// <summary>
    /// Subsurface samples. The ground is a layer of loose soil (regolith) a few tenths of a metre thick on top of bedrock; both
    /// are deterministic per surface cell of <see cref="CellSize"/> m, so one hole per cell. A drill reaches no deeper than its
    /// <see cref="ModuleDefinition.drillDepth"/>: a shallow one brings up soil, a deep one cuts into the rock. The core is a
    /// cylinder of radius <see cref="CoreRadius"/>: its mass is ρ·π·r²·L and the drilling energy is the specific energy of the
    /// material times the volume cut, E = e·π·r²·L. The result goes through <see cref="SampleCollection"/> like any sample.
    /// </summary>
    public static class DrillCores
    {
        public const double CellSize = 30.0;
        /// <summary>Radius of the cut core [m].</summary>
        public const double CoreRadius = 0.02;
        /// <summary>Hole diameter cut by the bit around the core, as an area factor (kerf) — the bit removes this much more than the core.</summary>
        public const double KerfFactor = 2.0;
        public const double SoilDensity = 1500.0, RockDensity = 2800.0;
        /// <summary>Specific cutting energy [J/m³]: loose regolith and basalt-like bedrock (rotary-percussive drilling).</summary>
        public const double SoilCuttingEnergy = 5.0e6, RockCuttingEnergy = 2.0e8;
        /// <summary>Share of the drill's electric power that reaches the bit.</summary>
        public const double DrillEfficiency = 0.5;
        /// <summary>Setting up, lowering and stowing the core [s].</summary>
        public const double HandlingTime = 8.0;
        /// <summary>Holes deeper than this sometimes meet rare minerals in the bedrock [m].</summary>
        public const double RareDepth = 1.0;

        /// <summary>The core a drill of <paramref name="drillDepth"/> metres can take at a planet-fixed direction; null without a drill or over water.</summary>
        public static DrillCore At(string planetId, double radius, Vector3d fixedDirection, double drillDepth, Func<Vector3d, bool> isWater = null)
        {
            if (drillDepth <= 0.0 || radius <= 0.0) return null;
            Vector3d dir = fixedDirection.Normalized;
            if (isWater != null && isWater(dir)) return null;
            PlanetRotation.LatLonFromDirection(dir, out double lat, out double lon);
            double cell = CellSize / radius;
            long i = (long)Math.Floor(lat / cell), j = (long)Math.Floor(lon / cell);
            var rnd = new DeterministicRandom(DeterministicRandom.Mix(Hash(planetId) ^ 0x5bd1e995UL, (ulong)(i * 83492791L) ^ (ulong)(j * 50331653L)));
            double soil = rnd.Range(0.08, 0.6);
            double rareRoll = rnd.NextDouble();
            double mineralRoll = rnd.NextDouble();

            double depth = drillDepth;
            double soilCut = Math.Min(depth, soil), rockCut = Math.Max(0.0, depth - soil);
            double area = Math.PI * CoreRadius * CoreRadius;
            double mass = (SoilDensity * soilCut + RockDensity * rockCut) * area;
            var site = new SampleSite { Id = "core:" + planetId + ":" + i + ":" + j, PlanetId = planetId, FixedDirection = dir, Mass = mass };
            bool home = planetId == HomeSystemCatalog.HomeWorldId;
            double valueFactor = home ? 0.3 : 1.6;
            if (rockCut <= 0.0)
            {
                bool mineral = mineralRoll < 0.3;
                site.Category = mineral ? SampleCategory.MineralSoil : SampleCategory.Soil;
                site.Rarity = mineral ? SampleRarity.Uncommon : SampleRarity.Common;
                site.Required = SampleKinds.Soil;
                site.Name = mineral ? Loc.T("Керн минерализованного грунта") : Loc.T("Керн грунта");
            }
            else if (depth >= RareDepth && rareRoll < 0.12)
            {
                site.Category = SampleCategory.RareGeology;
                site.Rarity = rareRoll < 0.02 ? SampleRarity.Exceptional : SampleRarity.Rare;
                site.Required = SampleKinds.Rock | SampleKinds.Soil | SampleKinds.Rare;
                site.Name = Loc.T("Керн с редкими минералами коренной породы");
            }
            else
            {
                bool mineral = mineralRoll < 0.35;
                site.Category = mineral ? SampleCategory.Mineral : SampleCategory.Rock;
                site.Rarity = mineral ? SampleRarity.Uncommon : SampleRarity.Common;
                site.Required = SampleKinds.Rock | SampleKinds.Soil;
                site.Name = mineral ? Loc.T("Керн с минеральными жилами") : Loc.T("Керн коренной породы");
            }
            // Unweathered material from below the surface is worth more to a laboratory than what lies on top.
            double[] science = { 5.0, 15.0, 50.0, 150.0 };
            site.ScientificValue = science[(int)site.Rarity] * valueFactor * (1.5 + 0.5 * Math.Min(1.0, depth / 2.0));
            site.BasePrice = (int)Math.Round(90.0 * valueFactor * (0.6 + 0.4 * Math.Min(3.0, mass)) * (1.0 + 0.5 * Math.Min(1.0, depth / 2.0)));
            site.Description = SampleSiteGenerator.CategoryName(site.Category) + " · " + SampleSiteGenerator.RarityName(site.Rarity) + " · " +
                               Academy.AcademyNumber.Format(mass) + Loc.T(" кг · с глубины ") + Academy.AcademyNumber.Format(depth) + Loc.T(" м");
            return new DrillCore
            {
                Site = site, Depth = depth, SoilDepth = soil,
                Energy = CuttingEnergy(soilCut, rockCut)
            };
        }

        /// <summary>Energy at the bit to cut a hole through soil and rock [J]: e·A·L with the bit's area (core × kerf).</summary>
        public static double CuttingEnergy(double soilLength, double rockLength)
        {
            double area = Math.PI * CoreRadius * CoreRadius * KerfFactor;
            return area * (SoilCuttingEnergy * Math.Max(0.0, soilLength) + RockCuttingEnergy * Math.Max(0.0, rockLength));
        }

        /// <summary>Time to drill [s]: the energy at the bit over the power that reaches it, plus handling. ∞ without power.</summary>
        public static double DrillTime(double energy, double electricPower, double supply = 1.0)
        {
            double p = DrillEfficiency * Math.Max(0.0, electricPower) * MathD.Clamp(supply, 0.0, 1.0);
            return p > 0.0 ? HandlingTime + Math.Max(0.0, energy) / p : double.PositiveInfinity;
        }

        /// <summary>Electric power of all the drills on board [W] (each runs its own bit; the deepest decides the hole).</summary>
        public static double DrillPower(RocketBlueprint design, Func<string, bool> isUnlocked = null)
        {
            double p = 0.0;
            if (design?.payloadModules == null) return 0.0;
            foreach (string id in design.payloadModules)
            {
                ModuleDefinition m = ModuleCatalog.Find(id);
                if (m == null || m.mechanismKind != MechanismKind.Drill) continue;
                if (isUnlocked != null && !isUnlocked(m.id)) continue;
                p = Math.Max(p, m.powerDraw);
            }
            return p;
        }

        /// <summary>Whether a drill can take this core; the sample rules (capacity, kinds) are then <see cref="SampleCollection.Check"/>'s.</summary>
        public static CollectionCheck CanDrill(VehicleCapabilities caps)
        {
            if (caps == null || caps.DrillCount <= 0) return new CollectionCheck(false, Loc.T("Нет бура: поставьте БУ-1 или БУ-2 в ангаре."));
            return new CollectionCheck(true, "");
        }

        static ulong Hash(string s)
        {
            ulong h = 1469598103934665603UL;
            foreach (char ch in s ?? "")
            {
                h ^= ch;
                h *= 1099511628211UL;
            }
            return h;
        }
    }

    /// <summary>
    /// Lamps as light sources (photometry, SI). A lamp of luminous flux Φ [lm] into a cone of full angle θ gives the luminous
    /// intensity I = Φ/(2π(1 − cos θ/2)) [cd]; the ground at distance d is lit with E = I/d² [lx]. The lamps' power draw is
    /// already in the vehicle's systems consumption (<see cref="VehicleCapabilities.PowerConsumption"/>).
    /// </summary>
    public static class LampPhotometry
    {
        /// <summary>Lamps from this flux up are narrow searchlights [lm].</summary>
        public const double SearchlightFlux = 10000.0;
        public const double FloodBeamDegrees = 60.0, SearchlightBeamDegrees = 40.0;

        /// <summary>Full cone angle of a lamp [deg]: a wide floodlight, a narrow searchlight.</summary>
        public static double BeamAngleDegrees(double luminousFlux) => luminousFlux >= SearchlightFlux ? SearchlightBeamDegrees : FloodBeamDegrees;

        /// <summary>Luminous intensity on the axis [cd]: I = Φ/Ω, Ω = 2π(1 − cos(θ/2)).</summary>
        public static double Intensity(double luminousFlux, double beamDegrees)
        {
            double half = MathD.Clamp(beamDegrees, 1.0, 179.0) * 0.5 * MathD.DegToRad;
            double solidAngle = 2.0 * Math.PI * (1.0 - Math.Cos(half));
            return Math.Max(0.0, luminousFlux) / solidAngle;
        }

        /// <summary>Illuminance on a surface facing the lamp [lx]: E = I/d².</summary>
        public static double Illuminance(double intensity, double distance) => distance > 0.0 ? intensity / (distance * distance) : double.PositiveInfinity;

        /// <summary>Distance at which the illuminance falls to <paramref name="minimumLux"/> [m]: d = √(I/E).</summary>
        public static double Reach(double intensity, double minimumLux) => minimumLux > 0.0 ? Math.Sqrt(Math.Max(0.0, intensity) / minimumLux) : double.PositiveInfinity;
    }
}
