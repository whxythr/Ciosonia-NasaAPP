using System;
using SpaceSim.Vehicles;

namespace SpaceSim.Science
{
    /// <summary>A mapped quantity of a body's surface. The int values are saved: never renumber.</summary>
    public enum ScienceLayer
    {
        /// <summary>Surface temperature [K] (<see cref="SurfaceClimate"/>).</summary>
        Temperature = 0,
        /// <summary>Surface atmospheric pressure [Pa] (the body's atmosphere at the local terrain height; 0 = vacuum).</summary>
        Pressure = 1,
        /// <summary>Terrain: elevation [m], albedo, biome / liquid surface (<see cref="SpaceSim.Procedural.PlanetSurfaceSampler"/>).</summary>
        Terrain = 2
    }

    public static class ScienceLayers
    {
        /// <summary>All layers in display order.</summary>
        public static readonly ScienceLayer[] All = { ScienceLayer.Temperature, ScienceLayer.Pressure, ScienceLayer.Terrain };

        public const int Count = 3;

        /// <summary>Values stored per cell of a layer (Terrain: elevation, albedo, surface code).</summary>
        public static int Channels(ScienceLayer layer) => layer == ScienceLayer.Terrain ? 3 : 1;

        // Terrain channels
        public const int ElevationChannel = 0, AlbedoChannel = 1, SurfaceCodeChannel = 2;

        /// <summary>Terrain surface code: biome index + this when the surface is liquid.</summary>
        public const int LiquidFlag = 16;
    }

    /// <summary>Why a scanner is or is not collecting data right now.</summary>
    public enum ScannerStatus
    {
        Scanning,
        /// <summary>Below the instrument's lowest working altitude (footprint too small / too fast over the ground).</summary>
        TooLow,
        /// <summary>Above the instrument's highest working altitude (signal too weak).</summary>
        TooHigh,
        /// <summary>The ground resolution (altitude × IFOV) is coarser than one map cell.</summary>
        ResolutionTooCoarse,
        /// <summary>No electric power for the instrument.</summary>
        NoPower,
        /// <summary>Switched off by the player.</summary>
        Off,
        /// <summary>The vehicle is landed: a nadir scanner sees only the cell it stands on.</summary>
        Landed
    }

    /// <summary>
    /// Capabilities of one nadir-pointing remote-sensing instrument, taken from its catalog module. Angles in radians, lengths in metres.
    /// </summary>
    public sealed class ScannerSpec
    {
        public string ModuleId = "";
        public string Name = "";
        public ScienceLayer Layer;
        /// <summary>Half-angle of the field of view / swath, measured from nadir [rad].</summary>
        public double HalfAngle;
        /// <summary>Lowest altitude above the reference radius the instrument works at [m].</summary>
        public double MinAltitude;
        /// <summary>Highest altitude above the reference radius the instrument works at [m].</summary>
        public double MaxAltitude;
        /// <summary>Instantaneous field of view of one detector element (angular resolution) [rad]: ground resolution = h·IFOV.</summary>
        public double Ifov;
        /// <summary>Electric draw [W] (part of the vehicle's systems consumption).</summary>
        public double PowerDraw;
        /// <summary>[kg]</summary>
        public double Mass;

        public static ScannerSpec From(ModuleDefinition m)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            return new ScannerSpec
            {
                ModuleId = m.id,
                Name = m.name,
                Layer = m.scanLayer,
                HalfAngle = m.scanHalfAngle * Math.PI / 180.0,
                MinAltitude = Math.Max(0.0, m.scanMinAltitude),
                MaxAltitude = Math.Max(m.scanMinAltitude, m.scanMaxAltitude),
                Ifov = Math.Max(0.0, m.scanIfov),
                PowerDraw = m.powerDraw,
                Mass = m.dryMass
            };
        }

        /// <summary>Whether the instrument can map at <paramref name="altitude"/> [m] onto cells <paramref name="cellSize"/> [m] wide.</summary>
        public ScannerStatus Check(double altitude, double cellSize)
        {
            if (altitude < MinAltitude) return ScannerStatus.TooLow;
            if (altitude > MaxAltitude) return ScannerStatus.TooHigh;
            if (ScanGeometry.GroundSampleDistance(altitude, Ifov) > cellSize) return ScannerStatus.ResolutionTooCoarse;
            return ScannerStatus.Scanning;
        }
    }
}
