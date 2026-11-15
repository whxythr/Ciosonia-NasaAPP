using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using SpaceSim.Units;

namespace SpaceSim.Science
{
    /// <summary>
    /// Long-term mean surface temperature of a body as a function of place, built only from data the game already models.
    ///
    /// Reference temperature T₀ — the body's authored mean surface temperature (<see cref="AtmosphereProperties.surfaceTemperature"/>,
    /// the value the atmosphere model, the navigation window and the hazard rules use; it already contains the greenhouse effect).
    /// Only an airless body whose temperature was never authored (left at the class default 288.15 K) falls back to radiative
    /// equilibrium with the star: T_eq = T★·√(R★ / 2d)·(1 − A)^¼ (<see cref="EquilibriumTemperature"/>).
    ///
    /// Latitude — annual-mean insolation of a body with obliquity β (North &amp; Coakley 1979, second Legendre term):
    ///     s(x) = 1 + s₂·P₂(x),  x = sin φ,  P₂(x) = (3x² − 1)/2,  s₂ = −⅝·P₂(cos β)
    /// (Earth, β = 23.4°: s₂ ≈ −0.477, equator 1.24, poles 0.52 of the mean).
    ///
    /// Albedo — a darker patch absorbs more: factor (1 − A_local)/(1 − A_mean).
    ///
    /// Heat transport — a dense atmosphere evens out the contrasts. Share of the local radiative contrast that survives:
    ///     w = 1 / (1 + p_s / 10⁶ Pa)   (vacuum 1, Earth-like 0.91, Venus-like 7.5 MPa 0.12).
    ///     T_rad = T₀ · (1 + w·(s(x)·(1 − A)/(1 − Ā) − 1))^¼
    ///
    /// Height — convective atmospheres cool upwards at the dry adiabatic lapse rate Γ = g·M/c_p with c_p ≈ 3.5·R/M (diatomic gas),
    /// i.e. Γ = g·M/(3.5·R_gas), weighted by how convective the air is, p_s/(p_s + 10⁴ Pa):
    ///     T = T_rad − Γ_eff·h   (h = surface height above the reference radius; basins are warmer). Floor 3 K.
    /// </summary>
    public sealed class SurfaceClimate
    {
        /// <summary>Default of <see cref="AtmosphereProperties.surfaceTemperature"/>: "not authored" for an airless body.</summary>
        public const double UnsetTemperature = 288.15;
        /// <summary>Surface pressure at which the atmosphere removes half of the radiative contrast [Pa].</summary>
        public const double RedistributionPressure = 1.0e6;
        /// <summary>Surface pressure at which the lapse rate is half the dry adiabat [Pa].</summary>
        public const double ConvectionPressure = 1.0e4;
        public const double MinimumTemperature = 3.0;
        /// <summary>Bond albedo assumed for the equilibrium fallback when nothing better is known.</summary>
        public const double DefaultBondAlbedo = 0.3;

        public SurfaceClimate(PlanetPhysicalProperties properties, double referenceTemperature, double meanAlbedo)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            ReferenceTemperature = Math.Max(MinimumTemperature, referenceTemperature);
            MeanAlbedo = MathD.Clamp(meanAlbedo, 0.0, 0.95);
            AtmosphereProperties a = properties.atmosphere;
            bool air = a != null && a.hasAtmosphere && a.surfacePressure > 0.0;
            SurfacePressure = air ? a.surfacePressure : 0.0;
            RedistributionWeight = 1.0 / (1.0 + SurfacePressure / RedistributionPressure);
            InsolationP2 = InsolationP2Coefficient(properties.axialTiltDegrees * MathD.DegToRad);
            double dryAdiabat = air && a.meanMolarMass > 0.0
                ? properties.SurfaceGravity * a.meanMolarMass / (3.5 * PhysicalConstants.MolarGasConstant)
                : 0.0;
            LapseRate = dryAdiabat * SurfacePressure / (SurfacePressure + ConvectionPressure);
        }

        public double ReferenceTemperature { get; }
        public double MeanAlbedo { get; }
        /// <summary>Atmospheric pressure at the reference radius [Pa] (0 = airless).</summary>
        public double SurfacePressure { get; }
        /// <summary>w: share of the radiative contrast left after atmospheric heat transport (0..1).</summary>
        public double RedistributionWeight { get; }
        /// <summary>s₂ of the annual-mean insolation.</summary>
        public double InsolationP2 { get; }
        /// <summary>Effective temperature lapse rate [K/m].</summary>
        public double LapseRate { get; }

        /// <summary>s₂ = −⅝·P₂(cos β) for obliquity β [rad].</summary>
        public static double InsolationP2Coefficient(double obliquity)
        {
            double c = Math.Cos(obliquity);
            return -0.625 * (1.5 * c * c - 0.5);
        }

        /// <summary>Annual-mean insolation at latitude φ relative to the body's mean (≥ 0.05).</summary>
        public static double RelativeInsolation(double latitude, double p2Coefficient)
        {
            double x = Math.Sin(latitude);
            return Math.Max(0.05, 1.0 + p2Coefficient * (1.5 * x * x - 0.5));
        }

        /// <summary>Radiative equilibrium temperature of a fast rotator: T★·√(R★/(2d))·(1 − A)^¼ [K].</summary>
        public static double EquilibriumTemperature(double starTemperature, double starRadius, double distance, double bondAlbedo)
        {
            if (!(starTemperature > 0.0) || !(starRadius > 0.0) || !(distance > 0.0)) return 0.0;
            return starTemperature * Math.Sqrt(starRadius / (2.0 * distance)) * Math.Pow(1.0 - MathD.Clamp(bondAlbedo, 0.0, 0.99), 0.25);
        }

        /// <summary>
        /// T₀ of a body: its authored mean surface temperature, or — for an airless body left at the default — the radiative
        /// equilibrium temperature (pass 0 when unknown: the default is then used as is).
        /// </summary>
        public static double ReferenceFor(AtmosphereProperties atmosphere, double equilibriumTemperature)
        {
            if (atmosphere == null) return equilibriumTemperature > 0.0 ? equilibriumTemperature : UnsetTemperature;
            bool authored = atmosphere.hasAtmosphere || Math.Abs(atmosphere.surfaceTemperature - UnsetTemperature) > 1e-6;
            if (authored && atmosphere.surfaceTemperature > 0.0) return atmosphere.surfaceTemperature;
            return equilibriumTemperature > 0.0 ? equilibriumTemperature : atmosphere.surfaceTemperature;
        }

        /// <summary>Mean surface temperature [K] at latitude φ [rad], surface height h [m] and local albedo A.</summary>
        public double Temperature(double latitude, double surfaceHeight, double albedo)
        {
            double s = RelativeInsolation(latitude, InsolationP2);
            double absorb = MathD.Clamp((1.0 - MathD.Clamp(albedo, 0.0, 0.99)) / (1.0 - MeanAlbedo), 0.2, 4.0);
            double forcing = Math.Max(0.02, 1.0 + RedistributionWeight * (s * absorb - 1.0));
            double t = ReferenceTemperature * Math.Pow(forcing, 0.25) - LapseRate * surfaceHeight;
            return Math.Max(MinimumTemperature, t);
        }
    }

    /// <summary>Every quantity the instruments can measure at one surface point.</summary>
    public struct SurfaceReading
    {
        public double Temperature;
        public double Pressure;
        /// <summary>Solid ground height above the reference radius [m] (may lie under a liquid).</summary>
        public double Elevation;
        /// <summary>Physical surface height (top of a liquid where there is one) [m].</summary>
        public double SurfaceHeight;
        public double Albedo;
        public Biome Biome;
        public bool Liquid;
    }

    /// <summary>
    /// What the science instruments read off a body: the actual planet model (atmosphere, terrain, albedo, rotation) plus the
    /// <see cref="SurfaceClimate"/>. Deterministic and thread-safe (no state changes after construction).
    /// </summary>
    public sealed class PlanetScienceModel
    {
        public PlanetScienceModel(string bodyId, PlanetModel model, double referenceTemperature)
        {
            BodyId = bodyId ?? "";
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Sampler = model.Surface as PlanetSurfaceSampler;
            Climate = new SurfaceClimate(model.Properties, referenceTemperature, MeanAlbedo(Sampler));
        }

        public string BodyId { get; }
        public PlanetModel Model { get; }
        /// <summary>Procedural surface (null for a smooth reference sphere: elevation 0, albedo <see cref="SurfaceClimate.DefaultBondAlbedo"/>).</summary>
        public PlanetSurfaceSampler Sampler { get; }
        public SurfaceClimate Climate { get; }
        public bool HasAtmosphere => !(Model.Atmosphere is VacuumAtmosphere);

        /// <summary>Linear luminance of an albedo colour.</summary>
        public static double Luminance(ColorRGB c) => MathD.Clamp(0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b, 0.0, 1.0);

        /// <summary>Mean albedo over 256 evenly spread points (Fibonacci sphere).</summary>
        static double MeanAlbedo(PlanetSurfaceSampler sampler)
        {
            if (sampler == null) return SurfaceClimate.DefaultBondAlbedo;
            const int n = 256;
            double sum = 0.0, golden = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int i = 0; i < n; i++)
            {
                double z = 1.0 - (i + 0.5) * 2.0 / n, r = Math.Sqrt(1.0 - z * z), a = golden * i;
                sum += Luminance(sampler.Sample(new Vector3d(r * Math.Cos(a), r * Math.Sin(a), z)).Albedo);
            }
            return sum / n;
        }

        /// <summary>All quantities at a planet-fixed unit direction.</summary>
        public SurfaceReading Read(Vector3d fixedDirection)
        {
            Vector3d d = fixedDirection.Normalized;
            var reading = new SurfaceReading();
            if (Sampler != null)
            {
                SurfaceSample s = Sampler.Sample(d);
                reading.Elevation = s.TerrainHeight;
                reading.SurfaceHeight = s.SurfaceHeight;
                reading.Albedo = Luminance(s.Albedo);
                reading.Biome = s.Biome;
                reading.Liquid = s.IsWater;
            }
            else
            {
                reading.Elevation = reading.SurfaceHeight = Model.Surface.SurfaceHeight(d);
                reading.Albedo = SurfaceClimate.DefaultBondAlbedo;
                reading.Biome = Biome.Rock;
            }
            double latitude = Math.Asin(MathD.Clamp(d.z, -1.0, 1.0));
            reading.Temperature = Climate.Temperature(latitude, reading.SurfaceHeight, reading.Albedo);
            // Pressure at the actual surface: the atmosphere profile at the local terrain (or liquid) height. Vacuum → exactly 0.
            reading.Pressure = HasAtmosphere ? Math.Max(0.0, Model.Atmosphere.Pressure(reading.SurfaceHeight)) : 0.0;
            return reading;
        }

        /// <summary>Writes the channels of <paramref name="layer"/> for one point into <paramref name="into"/>.</summary>
        public void Measure(ScienceLayer layer, Vector3d fixedDirection, float[] into)
        {
            SurfaceReading r = Read(fixedDirection);
            switch (layer)
            {
                case ScienceLayer.Temperature: into[0] = (float)r.Temperature; break;
                case ScienceLayer.Pressure: into[0] = (float)r.Pressure; break;
                default:
                    into[ScienceLayers.ElevationChannel] = (float)r.Elevation;
                    into[ScienceLayers.AlbedoChannel] = (float)r.Albedo;
                    into[ScienceLayers.SurfaceCodeChannel] = (int)r.Biome + (r.Liquid ? ScienceLayers.LiquidFlag : 0);
                    break;
            }
        }
    }
}
