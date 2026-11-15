using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Procedural;
using SpaceSim.Units;

namespace SpaceSim.Planets
{
    /// <summary>Engine-independent description of one body for building a system.</summary>
    public sealed class CelestialBodyPreset
    {
        public string Id;
        public string DisplayName;
        /// <summary>Id of the body this one orbits; null = the star.</summary>
        public string ParentId;
        public string Description;
        public PlanetPhysicalProperties Physical;
        public PlanetGenerationSettings Generation;
        public AtmosphereOpticalProperties Optics;
        public OrbitProperties Orbit;
        public double LaunchLatitude;
        public double LaunchLongitude;
        /// <summary>Radiation / acid danger levels (see HazardModel).</summary>
        public EnvironmentHazards Hazards = new EnvironmentHazards();
    }

    /// <summary>
    /// The game's home system (data supplied by the designer): Teonan, Quoris, Hermeous Mora and its irregular moon.
    ///
    /// Notes on interpretation:
    /// • "Prot" is used as the sidereal rotation period.
    /// • The star's mass is not given; it is derived from Kepler's third law, M = 4π²a³/(G·T²), using the
    ///   given a and T of Hermeous Mora (≈ 5.19·10³⁰ kg ≈ 2.6 M☉). All three planets agree within 1 %.
    ///
    /// Orbits (designer data of 2026-11-15: pericentre Dp, apocentre Da and a for every planet and the moons Aegis, Kolimpri,
    /// Tromos, Lin and Tiafoe; inclinations):
    /// • e = (Da − Dp)/(Da + Dp); the quoted a is used (it equals (Dp + Da)/2 to the quoted digits; differences are noted per body).
    /// • Periods: where a period was quoted it is kept and compared with Kepler's third law (all agree within 0.5 %, noted per
    ///   body); otherwise period = 0 and KeplerOrbit derives it from the parent's μ.
    /// • Inclinations are measured from the simulation's reference plane (inertial XY, "ecliptic" in the physics code; read it
    ///   as the star's equator). Every planet has i = <see cref="PlanetInclination"/> (3.17°) and the Mocha-Mocha–Dott barycentre
    ///   5.46°. The nodes were not given: every orbit has Ω = 0 and ω = 0 (node line and pericentres along +X), the convention
    ///   the Eremus moons already used. So the planets share one plane tilted 3.17° — as the Solar System's planets lie ≈ 6°
    ///   from the Sun's equator around one common plane — and transfers between them need no plane change; the binary is
    ///   2.29° out of that plane. Moon inclinations are measured from the same reference plane (not from the parent's orbit).
    /// • Spin axes: PlanetRotation tilts the axis about the same X axis, so with Ω = 0 the angle between the spin axis and the
    ///   orbit normal is |axialTilt − i|. The designer's obliquities are relative to the body's own orbit, so a freely
    ///   rotating planet gets axialTilt = i + obliquity (<see cref="TiltFromObliquity"/>), and a tidally locked body
    ///   axialTilt = i (spin axis perpendicular to its orbit). SurfaceClimate still reads axialTiltDegrees as the obliquity
    ///   (e.g. 25.67° instead of 22.5° for Ciosonia) — a known approximation.
    /// </summary>
    public static partial class HomeSystemCatalog
    {
        const double Day = 86400.0;
        const double AU = PhysicalConstants.AstronomicalUnit;
        /// <summary>Local solar time at the launch pads when the game starts [h].</summary>
        const double StartHour = 9.5;
        /// <summary>Position of the start planet on its orbit at t = 0 [deg] (its moons see the star in the same direction).</summary>
        const double HomeOrbitAngle = 40.0;

        public const string TeonanId = "teonan";
        public const string QuorisId = "quoris";
        public const string HermeousMoraId = "hermeous-mora";
        public const string MoraMoonId = "hermeous-mora-i";
        /// <summary>The habitable fourth planet where the game starts.</summary>
        public const string HomeWorldId = "home";
        /// <summary>Inner moon of the start planet (reddish, cratered, tidally locked).</summary>
        public const string KolimpriId = "kolimpri";
        /// <summary>Outer moon of the start planet (thin argon air, smooth, one canyon).</summary>
        public const string TromosId = "tromos";
        /// <summary>Fifth planet: lakes of liquid CO₂, red tholin dust in the north, ice in the south.</summary>
        public const string TholinId = "tholin";
        /// <summary>Sixth planet: the orange mega-Jupiter, the largest body of the system.</summary>
        public const string EremusId = "eremus";

        /// <summary>
        /// Version of the preset data. Saved home-system assets carry the version they were generated from; bump this when
        /// presets gain bodies or fields that older assets would silently lack (the editor then regenerates them).
        /// 1 = moons Kolimpri/Tromos, hazards, cloud tints, albedo maps, canyons, violet-sea colours.
        /// 2 = fifth and sixth planets (Tholin, Eremus), gas-giant bands.
        /// 3 = the eight moons of Eremus (HomeSystemCatalog.EremusMoons.cs), Eremus' obliquity 4.15°.
        /// 4 = names Lumini A / Ciosonia / Aegis, Tholin's moons Lin and Tiafoe, the Ciosonia artist map and its new pad.
        /// 5 = artist colour maps for all remaining bodies (ArtistMaps), Coloss' seas from its map.
        /// 6 = designer orbits (apsides, inclinations) for the planets and the moons of Ciosonia, Mora and Tholin; the double
        ///     planet Mocha-Mocha + Dott (HomeSystemCatalog.Binary.cs).
        /// </summary>
        public const int DataVersion = 6;

        /// <summary>Inclination of every planet's orbit to the reference plane [deg] (designer data).</summary>
        public const double PlanetInclination = 3.17;

        /// <summary>e = (Da − Dp)/(Da + Dp) from the pericentre and apocentre distances (any unit).</summary>
        public static double EccentricityFromApsides(double pericentre, double apocentre) =>
            (apocentre - pericentre) / (apocentre + pericentre);

        /// <summary>
        /// Axial tilt about the reference X axis that gives an obliquity <paramref name="obliquity"/> to an orbit of inclination
        /// <paramref name="inclination"/> with Ω = 0 (both rotations are about X, so the angles add).
        /// </summary>
        static double TiltFromObliquity(double inclination, double obliquity) => inclination + obliquity;

        /// <summary>True anomaly [deg] for a mean anomaly [deg] and eccentricity (Newton on Kepler's equation).</summary>
        public static double TrueAnomalyDegrees(double meanAnomalyDegrees, double eccentricity)
        {
            double M = meanAnomalyDegrees * MathD.DegToRad, E = M;
            for (int i = 0; i < 30; i++) E -= (E - eccentricity * Math.Sin(E) - M) / (1.0 - eccentricity * Math.Cos(E));
            double nu = 2.0 * Math.Atan2(Math.Sqrt(1.0 + eccentricity) * Math.Sin(0.5 * E), Math.Sqrt(1.0 - eccentricity) * Math.Cos(0.5 * E));
            // Keep the same turn as the mean anomaly, so callers can compare the two directly.
            double deg = nu * MathD.RadToDeg;
            return deg + 360.0 * Math.Round((meanAnomalyDegrees - deg) / 360.0);
        }

        public static double StarMassFromKepler(double semiMajorAxis, double period) =>
            4.0 * Math.PI * Math.PI * Math.Pow(semiMajorAxis, 3) / (PhysicalConstants.GravitationalConstant * period * period);

        public static StarProperties Star() => new StarProperties
        {
            name = "Люмини A (Lumini A)",
            mass = StarMassFromKepler(0.427 * AU, 63.1 * Day),
            radius = 1.2e9,
            surfaceTemperature = 4900.0,
            color = new ColorRGB(1.0f, 0.88f, 0.70f)
        };

        public static List<CelestialBodyPreset> Bodies()
        {
            var list = new List<CelestialBodyPreset> { Teonan(), Quoris(), HermeousMora() };
            list.Add(MoraMoon(list[2].Physical.GravitationalParameter));
            CelestialBodyPreset home = HomeWorld();
            list.Add(home);
            list.Add(Kolimpri(home.Physical.GravitationalParameter));
            list.Add(Tromos(home.Physical.GravitationalParameter));
            CelestialBodyPreset tholin = Tholin();
            list.Add(tholin);
            AddTholinMoons(list, tholin.Physical.GravitationalParameter);
            CelestialBodyPreset eremus = Eremus();
            list.Add(eremus);
            AddEremusMoons(list, eremus.Physical.GravitationalParameter);
            AddDoublePlanet(list);
            foreach (CelestialBodyPreset b in list)
                if (b.Generation != null && !b.Generation.UsesAlbedoMap && ArtistMaps.TryGetValue(b.Id, out string key))
                    b.Generation.albedoMap = key;
            return list;
        }

        /// <summary>
        /// Artist equirectangular colour maps (Resources/PlanetMaps/&lt;key&gt;.png, set 2026-11-14) for the bodies whose
        /// presets do not name one themselves. The map gives the colour; relief stays procedural. Which picture belongs to
        /// which body was matched by the descriptions (the files came unnamed) — swap a key here to reassign.
        /// </summary>
        static readonly Dictionary<string, string> ArtistMaps = new Dictionary<string, string>
        {
            { QuorisId, "quoris" }, { HermeousMoraId, "hermeous-mora" }, { MoraMoonId, "aegis" },
            { LinId, "lin" }, { TiafoeId, "tiafoe" }, { EremusId, "eremus" },
            { OriId, "ori" }, { OinId, "oin" }, { BifurId, "bifur" }, { NevermourId, "nevermour" },
            { DwalinId, "dwalin" }, { BalinId, "balin" }, { ColossId, "coloss" }, { GandalfId, "gandalf" }
        };

        /// <summary>
        /// Tholin (Золин) — fifth planet, an analogue for Mars missions (designer data: M = 1.56 M⊕ = 9.36·10²⁴ kg,
        /// R = 7 244 420 m, ρ = 5880 kg/m³, g = 11.9 m/s², T = 37.8 °C, day 1.75 d, year 182 d, a = 0.865 AU,
        /// pericentre 0.798 AU, apocentre 0.931 AU, i = 3.17°). M and R reproduce ρ and g to 0.1 %; Kepler with the star's mass
        /// gives 181.93 d for a = 0.865 AU (0.04 % below the quoted 182 d, which is kept). e = (0.931 − 0.798)/(0.931 + 0.798)
        /// = 0.0769; (Dp + Da)/2 = 0.8645 AU, the quoted a = 0.865 AU is used (the apsides it implies, 0.7985 / 0.9315 AU, are
        /// within 0.05 % of the quoted ones). (Before 2026-11-15 the pericentre was 0.799 AU.)
        /// Interpretation (not given):
        /// • Liquid CO₂ exists only between its triple point (−56.6 °C, 5.18 bar) and critical point (31.0 °C, 73.8 bar). At
        ///   the quoted mean of 37.8 °C no CO₂ can be liquid at any pressure, so the lakes must lie where it is colder: the
        ///   atmosphere is taken as 60 bar of CO₂ (boiling point ≈ 22 °C), 37.8 °C is the global mean, and the lakes fill
        ///   the cooler basins and high latitudes. 60 bar is above the suit limit (2 atm): no EVA, the arm still samples.
        /// • Axial tilt 30° (only "the tilt causes the icy south" is given).
        /// • Colours, the tholin north and the icy south come from the artist map Resources/PlanetMaps/tholin; its dark
        ///   regions become basins (albedoMapRelief), and sea level 0 fills about half the surface with lakes.
        /// • The optical coefficients are tuned to the artist render (hazy cream disk), not derived from 60 bar of CO₂, which
        ///   would hide the surface completely (as Hermeous Mora's are tuned).
        /// </summary>
        static readonly double TholinEccentricity = EccentricityFromApsides(0.798, 0.931);

        static CelestialBodyPreset Tholin()
        {
            const double orbitAngle = 200.0, padLat = 38.0, padLon = 12.0;
            var a = new AtmosphereProperties
            {
                hasAtmosphere = true,
                surfacePressure = 6.0e6,            // assumption: keeps CO₂ liquid below ≈ 22 °C
                composition = "CO₂ ≈ 95 %, N₂ ≈ 3 %, Ar ≈ 2 % (давление не задано — допущение 60 бар, чтобы CO₂ мог быть жидким)",
                surfaceTemperature = 310.95,        // 37.8 °C
                meanMolarMass = 0.04345,
                coRotationFactor = 1.0
            };
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 7244420.0,
                mass = 9.36e24,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 1.75 * Day,
                axialTiltDegrees = TiltFromObliquity(PlanetInclination, 30.0),
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(TrueAnomalyDegrees(orbitAngle, TholinEccentricity), padLon, StartHour),
                spaceBoundaryAltitude = 120000.0,   // ≈ 24 scale heights: p there ≈ Earth's at 100 km
                atmosphere = a
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            a.scaleHeight = AtmosphericModel.IsothermalScaleHeight(a.surfaceTemperature, a.meanMolarMass, p.SurfaceGravity);
            a.surfaceDensity = AtmosphericModel.IdealGasDensity(a.surfacePressure, a.meanMolarMass, a.surfaceTemperature);
            var optics = new AtmosphereOpticalProperties
            {
                // A dense CO₂ sky is not Earth-blue: fine dust and CO₂ ice haze scatter almost grey, with only a faint dull-blue
                // Rayleigh share left (visual tuning: nearly flat coefficients, blue only ≈ 30 % above red).
                rayleighScattering = new Vector3d(1.6e-5, 1.75e-5, 2.1e-5),
                mieScattering = 1.2e-5, mieExtinctionRatio = 1.3, mieScaleHeight = 3000.0, mieAnisotropy = 0.75
            };
            var g = new PlanetGenerationSettings
            {
                seed = 50582,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = true,
                seaLevel = 0.0,
                continentFrequency = 1.4, continentBias = 0.0, continentAmplitude = 700, oceanDepthAmplitude = 700, warpStrength = 0.15,
                mountainFrequency = 3.0, mountainAmplitude = 900,
                detailFrequency = 150, detailAmplitude = 60,
                craterCount = 40, craterMinRadius = 500, craterMaxRadius = 20000, craterDepthRatio = 0.08,
                lowland = new ColorRGB(0.42f, 0.20f, 0.18f),      // procedural fallback: tholin-red lowlands
                highland = new ColorRGB(0.66f, 0.64f, 0.62f),     // pale frosted highlands
                stain = new ColorRGB(0.45f, 0.17f, 0.15f), stainCoverage = 0.25, stainFrequency = 4.0,
                craterFloorDarkening = 0.12, moistureFrequency = 2.5,
                // Liquid CO₂ is colourless (n ≈ 1.2): the lake bed shows through, darkened by depth.
                shallowOcean = new ColorRGB(0.60f, 0.60f, 0.64f),
                deepOcean = new ColorRGB(0.06f, 0.06f, 0.08f),
                cloudCoverage = 0.08, cloudTint = new ColorRGB(1f, 1f, 1f), // thin dry-ice cirrus
                albedoMap = "tholin",
                albedoMapDetail = 0.3,
                albedoMapRelief = 2500.0
            };
            return new CelestialBodyPreset
            {
                Id = TholinId, DisplayName = "Золин (Tholin)",
                Description = "Пятая планета, создана для отработки марсианских миссий. Половина поверхности — озёра жидкого углекислого газа. Северное полушарие покрыто красной пылью — золином (сложные органические полимеры, как на Хароне), южное — льдом из-за наклона оси. 1.56 M⊕, g = 11.9 м/с², 37.8 °C, сутки 1.75 земных, год 182 дня.",
                Physical = p, Generation = g, Optics = optics,
                Orbit = new OrbitProperties
                {
                    semiMajorAxis = 0.865 * AU, eccentricity = TholinEccentricity, inclinationDegrees = PlanetInclination,
                    period = 182.0 * Day, meanAnomalyAtEpochDegrees = orbitAngle
                },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 1 } // dense air shields the ground (only above it)
            };
        }

        /// <summary>
        /// Eremus (Еремус) — sixth planet, the only gas giant, a mega-Jupiter (designer data: M = 864 M⊕, R = 12.9 R⊕,
        /// ρ = 2208 kg/m³, g = 50.8 m/s², T = −63.6 °C, day 0.329 d, year 664 d, a = 2.03 AU, pericentre 1.98 AU,
        /// apocentre 2.12 AU, polar flattening 3.98 %, 17 moons of which 7 are major).
        /// With M⊕ = 5.9722·10²⁴ kg and R⊕ = 6 371 000 m: ρ = 2219 kg/m³ and g = 51.0 m/s² (0.5 % above the quoted values).
        /// Orbit: the apsides give a = (1.98 + 2.12)/2 = 2.05 AU and e = 0.034, and Kepler with the star's mass gives
        /// 664 d for a = 2.05 AU (but 654 d for 2.03 AU), so a = 2.05 AU is used — it agrees with both the period and
        /// the apsides. i = 3.17° like every planet (added 2026-11-15; a, e and T unchanged). Not modelled: the flattening
        /// (the shape and gravity stay spherical, no J2).
        /// The eight major moons are in HomeSystemCatalog.EremusMoons.cs; the axial tilt (not given) is 4.15° to the reference
        /// plane so that they orbit in its equatorial plane (0.98° to its own orbit now that the orbit is inclined 3.17°; the
        /// moons' inclinations, also measured from the reference plane, are left as they were).
        /// The reference radius is the 1-bar level; there is no solid surface — the "ground" is the cloud tops.
        /// </summary>
        static CelestialBodyPreset Eremus()
        {
            const double orbitAngle = EremusOrbitAngle, padLat = 0.0, padLon = -30.0;
            const double earthMass = 5.9722e24, earthRadius = 6371000.0;
            var a = new AtmosphereProperties
            {
                hasAtmosphere = true,
                surfacePressure = 1.0e5,            // the 1-bar reference level
                composition = "H₂ ≈ 86 %, He ≈ 13.6 %, CH₄, NH₃ (как у Юпитера: состав не задан)",
                surfaceTemperature = 209.55,        // −63.6 °C
                meanMolarMass = 0.00234,
                coRotationFactor = 1.0
            };
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 12.9 * earthRadius,
                mass = 864.0 * earthMass,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 0.329 * Day,
                axialTiltDegrees = EremusObliquity, // not given: the regular moons' plane (Jupiter: 3.1°)
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(orbitAngle, padLon, StartHour),
                spaceBoundaryAltitude = 250000.0,   // ≈ 17 scale heights above the 1-bar level
                atmosphere = a
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            a.scaleHeight = AtmosphericModel.IsothermalScaleHeight(a.surfaceTemperature, a.meanMolarMass, p.SurfaceGravity);
            a.surfaceDensity = AtmosphericModel.IdealGasDensity(a.surfacePressure, a.meanMolarMass, a.surfaceTemperature);
            // H₂ scatters ≈ 0.22 × as much per molecule as air; at 1 bar and 210 K there are 1.36 × as many molecules.
            double numberRatio = (a.surfacePressure / a.surfaceTemperature) / (101325.0 / 288.15);
            var optics = new AtmosphereOpticalProperties
            {
                // Pure H₂ would scatter blue ((0.22 · numberRatio) × Earth), but like Jupiter's the upper air is laced with
                // chromophores that absorb blue: the limb glows orange (visual tuning, red > green > blue).
                rayleighScattering = new Vector3d(2.6e-6, 1.6e-6, 0.55e-6) * numberRatio,
                mieScattering = 2.0e-6, mieExtinctionRatio = 1.2, mieScaleHeight = 20000.0, mieAnisotropy = 0.7,
                // Orange photochemical haze above the clouds.
                hazeScattering = new Vector3d(5.5e-6, 3.0e-6, 1.0e-6), hazeAltitude = 60000.0, hazeThickness = 20000.0
            };
            var g = new PlanetGenerationSettings
            {
                seed = 120577,
                surfaceStyle = SurfaceStyle.GasGiant,
                hasOcean = false,
                // No solid surface: the 1-bar level is a smooth sphere.
                continentAmplitude = 0, oceanDepthAmplitude = 0, mountainAmplitude = 0, detailAmplitude = 0, detailOctaves = 0,
                craterCount = 0, warpFrequency = 1.5, warpStrength = 0.06,
                lowland = new ColorRGB(0.85f, 0.42f, 0.18f),      // belts: deep orange
                highland = new ColorRGB(0.98f, 0.72f, 0.46f),     // zones: pale apricot
                stain = new ColorRGB(0.72f, 0.30f, 0.12f), stainCoverage = 0.04, stainFrequency = 5.0, // rust-red ovals
                bandCount = 8.0, bandTurbulence = 1.4, bandStretch = 10.0,
                moistureFrequency = 2.0,
                cloudCoverage = 0.0                               // the bands are the cloud tops
            };
            return new CelestialBodyPreset
            {
                Id = EremusId, DisplayName = "Еремус (Eremus)",
                Description = "Шестая планета, единственный газовый гигант — мегаюпитер и самый большой объект системы. Оранжевый, состоит преимущественно из водорода и гелия. 864 M⊕, 12.9 R⊕, g = 50.8 м/с², −63.6 °C на уровне 1 бар, сутки 0.329 земных, год 664 дня, полярное сжатие 3.98 %. 17 спутников, крупные из них: Ори, Оин, Бифур, Невермор, Двалин, Балин, Колосс и Гандальф. Твёрдой поверхности нет: «площадка» — на уровне облаков.",
                Physical = p, Generation = g, Optics = optics,
                Orbit = new OrbitProperties
                {
                    semiMajorAxis = 2.05 * AU, eccentricity = EccentricityFromApsides(1.98, 2.12), inclinationDegrees = PlanetInclination,
                    period = 664.0 * Day, meanAnomalyAtEpochDegrees = orbitAngle
                },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 2 } // intense radiation belts of a giant magnetosphere
            };
        }

        /// <summary>
        /// Kolimpri — inner moon of the start planet (designer data: M = 1.43·10²¹ kg, R = 427 406 m, ρ = 4440 kg/m³,
        /// g = 0.531 m/s², a = 56 896 095 m, rotation = revolution = 1.7 d, −111…−156 °C).
        /// The given M and R give ρ = 4373 kg/m³ and g = 0.522 m/s² (1.5–2 % below the quoted values); the mass is kept.
        /// Orbit (2026-11-15): Dp = 54 684 487 m, Da = 59 092 271 m, a = 56 888 489 m → e = 0.0387, i = 6.51° (to the reference
        /// plane). (Dp + Da)/2 = 56 888 379 m: 110 m (2·10⁻⁶) below the quoted a, which is used. (Before: circular, 56 896 095 m.)
        /// The period follows from Kepler with the start planet's μ: 1.703 d (the quoted 1.7 d agrees).
        /// Tidally locked: the map's central meridian (longitude 0) faces the planet on average — exactly at pericentre and
        /// apocentre, with a libration of up to ±(2e + 1.25e²) ≈ ±4.5° between them; the spin axis is the orbit normal.
        /// Colours come from the artist map Resources/PlanetMaps/kolimpri (iron-rich pink highlands, dark grey "seas");
        /// without it the procedural colours below approximate the map.
        /// </summary>
        static CelestialBodyPreset Kolimpri(double parentMu)
        {
            // 60° from the anti-star direction of the planet (home orbit angle 40° + 0°): the near side and the pad
            // (≈ 35° from the sub-solar point) are in the afternoon sun at the start; from the planet it is gibbous.
            const double orbitAngle = 100.0, radius = 427406.0, inclination = 6.51;
            var orbit = new OrbitProperties
            {
                semiMajorAxis = 56888489.0, eccentricity = EccentricityFromApsides(54684487.0, 59092271.0),
                inclinationDegrees = inclination, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true
            };
            double period = OrbitalMechanics.CircularPeriod(parentMu, orbit.semiMajorAxis);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = radius,
                mass = 1.43e21,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = period,
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0,  // prime meridian faces the parent (mean direction)
                axialTiltDegrees = inclination,                    // spin axis = orbit normal
                spaceBoundaryAltitude = 10000.0,
                atmosphere = new AtmosphereProperties
                {
                    hasAtmosphere = false, surfaceDensity = 0.0, surfacePressure = 0.0,
                    surfaceTemperature = 139.65,  // mean of −111 °C … −156 °C
                    composition = "нет атмосферы"
                }
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            var g = new PlanetGenerationSettings
            {
                seed = 61519,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 1.3, continentAmplitude = 1500, oceanDepthAmplitude = 2000, warpStrength = 0.2,
                mountainFrequency = 3.5, mountainAmplitude = 1200,
                detailFrequency = 120, detailAmplitude = 80,
                // The map already shows the big craters; the generator adds many small ones for close-up relief.
                craterCount = 600, craterMinRadius = 400, craterMaxRadius = 25000, craterDepthRatio = 0.12,
                lowland = new ColorRGB(0.38f, 0.35f, 0.33f),
                highland = new ColorRGB(0.62f, 0.54f, 0.50f),
                stain = new ColorRGB(0.30f, 0.28f, 0.27f), stainCoverage = 0.30, stainFrequency = 2.5,
                craterFloorDarkening = 0.15, moistureFrequency = 3.0,
                albedoMap = "kolimpri",
                albedoMapDetail = 0.35,
                albedoMapRelief = 4000.0  // dark "seas" are basins up to ~1.5 km deep
            };
            return new CelestialBodyPreset
            {
                Id = KolimpriId, DisplayName = "Колимпри (Kolimpri)", ParentId = HomeWorldId,
                Description = "Ближний спутник стартовой планеты. Красновато-розовый из-за больших отложений железа на поверхности. Образовался, как Луна: при гипотетическом столкновении двух планет. Приливно захвачен, без атмосферы (близко к планете, малая гравитация). Вся поверхность в массивных кратерах, тёмно-серые участки — «моря». −111 … −156 °C, g = 0.53 м/с², сутки = обращение = 1.7 земных",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = orbit,
                LaunchLatitude = 8.0, LaunchLongitude = -25.0,  // in the big sea of the near side, the planet high in the sky
                Hazards = new EnvironmentHazards { radiationLevel = 1 } // outside the planet's magnetopause
            };
        }

        /// <summary>
        /// Tromos — outer moon of the start planet (designer data: M = 3.31·10²⁰ kg, R = 266 478 m, ρ = 4180 kg/m³,
        /// g = 0.311 m/s², a = 97 059 000 m, day 2.55 d, revolution 3.80 d, −42.6 °C, thin Ar air with some He and Ne).
        /// Interpretation: the surface pressure is not given — 100 Pa (≈ 0.1 % of the start planet) makes the air
        /// "slightly visible" (≈ 2 % of the start planet's Rayleigh optical depth). Composition taken as 90 % Ar, 6 % Ne,
        /// 4 % He → M = 37.3 g/mol. With g this small the isothermal scale height is ≈ 165 km (0.6 R).
        /// Physically such air would escape: Jeans parameter for Ar λ = (v_esc / v_th)² ≈ 1.7 (needs ≳ 20 to last), so it
        /// has to be replenished — the origin of the moon is unknown by design.
        /// Orbit (2026-11-15): Dp = 84 676 534 m, Da = 109 000 000 m, a = 96 890 115 m → e = 0.1256, i = 3.58° (to the reference
        /// plane). (Dp + Da)/2 = 96 838 267 m, 52 km (0.05 %) below the quoted a: Da looks rounded to 10⁶ m. The quoted a is used,
        /// so the modelled apsides are 84 722 km and 109 058 km. Kepler with the start planet's μ gives 3.785 d (the quoted
        /// revolution of 3.80 d is 0.4 % longer). (Before: circular, 97 059 000 m.)
        /// </summary>
        static CelestialBodyPreset Tromos(double parentMu)
        {
            const double orbitAngle = 75.0, radius = 266478.0, padLon = 40.0;
            var orbit = new OrbitProperties
            {
                semiMajorAxis = 96890115.0, eccentricity = EccentricityFromApsides(84676534.0, 109000000.0),
                inclinationDegrees = 3.58, meanAnomalyAtEpochDegrees = orbitAngle
            };
            var a = new AtmosphereProperties
            {
                hasAtmosphere = true,
                surfacePressure = 100.0,
                composition = "Ar ≈ 90 %, Ne ≈ 6 %, He ≈ 4 % (давление не задано — допущение 100 Па)",
                surfaceTemperature = 230.55,      // −42.6 °C
                meanMolarMass = 0.0373,
                coRotationFactor = 1.0
            };
            var p = new PlanetPhysicalProperties
            {
                meanRadius = radius,
                mass = 3.31e20,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 2.55 * Day,
                // Seen from a moon the star is where it is seen from the planet (its true anomaly + 180°): pad at 10:00.
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(HomeTrueAnomaly, padLon, 10.0),
                axialTiltDegrees = 0.0,             // not given: spin axis along the reference-plane normal
                spaceBoundaryAltitude = 300000.0, // ≈ 2 scale heights: the air is very extended on such a small body
                atmosphere = a
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            a.scaleHeight = AtmosphericModel.IsothermalScaleHeight(a.surfaceTemperature, a.meanMolarMass, p.SurfaceGravity);
            a.surfaceDensity = AtmosphericModel.IdealGasDensity(a.surfacePressure, a.meanMolarMass, a.surfaceTemperature);
            // Rayleigh scattering scales with the number density; argon scatters ≈ 0.92 × as much per molecule as air.
            double numberRatio = (a.surfacePressure / a.surfaceTemperature) / (101325.0 / 288.15);
            var optics = new AtmosphereOpticalProperties
            {
                rayleighScattering = new Vector3d(5.802e-6, 13.558e-6, 33.1e-6) * (0.92 * numberRatio),
                mieScattering = 0.0,
                // 100 Pa of O₂ is invisible from orbit and from the ground (Mars has 6× more): no sky shell or limb at all.
                visible = false,
                autoShellHeight = false,
                shellHeight = 3.0 * a.scaleHeight
            };
            var g = new PlanetGenerationSettings
            {
                seed = 83302,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                // Smooth plains: low, broad relief and only small craters.
                continentFrequency = 1.0, continentAmplitude = 800, oceanDepthAmplitude = 800, warpStrength = 0.25,
                mountainFrequency = 2.5, mountainAmplitude = 400,
                detailFrequency = 90, detailAmplitude = 30, detailOctaves = 7,
                craterCount = 25, craterMinRadius = 300, craterMaxRadius = 3000, craterDepthRatio = 0.08,
                lowland = new ColorRGB(0.55f, 0.54f, 0.52f),
                highland = new ColorRGB(0.74f, 0.73f, 0.71f),
                stain = new ColorRGB(0.36f, 0.35f, 0.34f), stainCoverage = 0.15, stainFrequency = 4.0,
                craterFloorDarkening = 0.1, moistureFrequency = 2.0,
                cloudCoverage = 0.0, // argon does not condense at −43 °C
                featureCanyons = new[]
                {
                    // One wide canyon, ≈ 330 km long, 35 km wide, 3.5 km deep.
                    new FeatureCanyon
                    {
                        startLatitudeDegrees = 18.0, startLongitudeDegrees = -60.0,
                        endLatitudeDegrees = 30.0, endLongitudeDegrees = 10.0,
                        width = 35000.0, depth = 3500.0
                    }
                },
                canyonDarkening = 0.3,
                albedoMap = "tromos",
                albedoMapDetail = 0.3,
                albedoMapRelief = 0.0  // the light patches of the map are haze, not highlands
            };
            return new CelestialBodyPreset
            {
                Id = TromosId, DisplayName = "Тромос (Tromos)", ParentId = HomeWorldId,
                Description = "Второй по дальности спутник стартовой планеты. Температура поверхности (−42.6 °C) выживаема для человека благодаря слегка видимой атмосфере из аргона с небольшой примесью гелия и неона. Больших кратеров нет, поверхность гладкая, есть один широкий каньон. Происхождение неизвестно. g = 0.31 м/с², сутки 2.55 земных, обращение 3.8 земных",
                Physical = p, Generation = g, Optics = optics,
                Orbit = orbit,
                LaunchLatitude = -10.0, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 1 } // only above the air (ρ/ρ₀ < 1 %)
            };
        }

        /// <summary>
        /// Fourth planet — the start. An Earth copy with lower mass and gravity; violet water (K-type star light),
        /// equatorial deserts, coastal mountains near the equator, poles ≈ −49 °C.
        /// Interpretation: axial tilt 22.5°; the second "11.5°" is taken as the magnetic-pole tilt (informational,
        /// together with B = 0.254 G and the magnetopause at 13 627 603 m). Oblateness 0.286 % is stored but not
        /// yet used by gravity (no J2) nor by the shape.
        /// Orbit (2026-11-15): Dp = 0.63 AU, Da = 0.71 AU, a = 0.67 AU → e = 0.0597, i = 3.17°. The quoted year of 124 d is kept;
        /// Kepler with the star's mass gives 124.02 d (0.02 %).
        /// </summary>
        static readonly double HomeEccentricity = EccentricityFromApsides(0.63, 0.71);
        /// <summary>True anomaly of the start planet at t = 0 [deg]: the direction of the star seen from it and its moons.</summary>
        static double HomeTrueAnomaly => TrueAnomalyDegrees(HomeOrbitAngle, HomeEccentricity);

        static CelestialBodyPreset HomeWorld()
        {
            // The pad is on the green plain of the western continent of the artist map (the old 28° N, 52° W is open sea there).
            const double orbitAngle = HomeOrbitAngle, padLat = 19.0, padLon = -50.5;
            // Early morning at the pad (not StartHour): the star stands low in the east, visible from the pad, with long shadows,
            // and climbs during the first hours of play (the day is 0.844 Earth days, so ≈ 18° per hour).
            const double startHour = 7.0;
            var a = new AtmosphereProperties
            {
                hasAtmosphere = true,
                surfacePressure = 101325.0,        // Earth-like (not given)
                composition = "N₂ ≈ 78 %, O₂ ≈ 21 %, Ar ≈ 1 % — как у Земли (допущение: состав не задан)",
                surfaceTemperature = 295.45,       // 22.3 °C
                meanMolarMass = 0.0289644,
                coRotationFactor = 1.0
            };
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 6370332.0,
                mass = 5.03e24,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 0.844 * Day,
                axialTiltDegrees = TiltFromObliquity(PlanetInclination, 22.5), // 22.5° to its own orbit
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(HomeTrueAnomaly, padLon, startHour),
                spaceBoundaryAltitude = 100000.0,
                atmosphere = a
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            a.scaleHeight = AtmosphericModel.IsothermalScaleHeight(a.surfaceTemperature, a.meanMolarMass, p.SurfaceGravity);
            a.surfaceDensity = AtmosphericModel.IdealGasDensity(a.surfacePressure, a.meanMolarMass, a.surfaceTemperature);
            var g = new PlanetGenerationSettings
            {
                seed = 40417,
                surfaceStyle = SurfaceStyle.Terrestrial,
                hasOcean = true,
                continentBias = -0.03,
                equatorialAridity = 0.45,
                coastalMountainBoost = 0.9,
                elevationCooling = 0.00008,
                // Violet seas: retinal-type pigment in the plankton absorbs green (500–600 nm) and back-scatters red and violet.
                // Luminance stays ocean-like (≈ 4 % linear, Earth's sea ≈ 0.3–3 %), only the chroma differs. The albedo must be
                // this saturated: at the old (0.07, 0.02, 0.16) the water body (≈ 0.4 % linear) was weaker than the 2 % Fresnel
                // reflection of the blue sky plus aerial perspective, and the sea rendered navy like Earth's. Blue outweighs red
                // ≈ 3:1 (linear) because the K-star's light is ≈ 3:1 red-heavy after the air: lit water reads purple (hue ≈ 285°),
                // not magenta. Sea ice is faintly lilac (pigment trapped in brine).
                deepOcean = new ColorRGB(0.32f, 0.08f, 0.54f),
                shallowOcean = new ColorRGB(0.44f, 0.22f, 0.68f),
                seaIce = new ColorRGB(0.84f, 0.80f, 0.90f),
                grassland = new ColorRGB(0.26f, 0.42f, 0.14f),
                forest = new ColorRGB(0.10f, 0.26f, 0.08f),
                desert = new ColorRGB(0.80f, 0.64f, 0.40f),
                craterCount = 6,
                // The artists' equirectangular map (Resources/PlanetMaps/ciosonia): it gives the continents, the coastline
                // (violet = sea) and the land colours; the generator adds relief and sub-pixel detail.
                albedoMap = "ciosonia",
                albedoMapDetail = 0.3,
                // 0.12: inland the continent field is ≈ 0.12 → lowlands of ≈ 600 m with mountains only where the noise lifts it
                // (0.25 put whole continents at 1.5–2 km with ridges to 5 km, and they read as snowfields from orbit).
                albedoMapCoastline = 0.12
            };
            return new CelestialBodyPreset
            {
                Id = HomeWorldId, DisplayName = "Циосония (Ciosonia)",
                Description = "Обитаемая стартовая планета: копия Земли с меньшей массой (0.842 M⊕) и g = 8.27 м/с². Фиолетовая вода (свет звезды K-класса), экваториальные пустыни, горы у экваториальных побережий, на полюсах до −49 °C. Сплюснутость 0.286 %, наклон оси 22.5°, магнитный полюс 11.5°, B = 0.254 Гс, магнитопауза 13 627 603 м. Сутки 0.844 земных, год 124 дня.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = new OrbitProperties
                {
                    semiMajorAxis = 0.67 * AU, eccentricity = HomeEccentricity, inclinationDegrees = PlanetInclination,
                    period = 124.0 * Day, meanAnomalyAtEpochDegrees = orbitAngle
                },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards() // magnetosphere + atmosphere: safe
            };
        }

        /// <summary>
        /// Rotation phase at t = 0 that puts a pad at <paramref name="padLongitude"/> at local solar time
        /// <paramref name="hour"/>. <paramref name="orbitAngleDeg"/> is the body's direction angle in its orbit plane at t = 0
        /// (the true anomaly — pass <see cref="TrueAnomalyDegrees"/> for an eccentric orbit, not the mean anomaly); the star is
        /// at that angle + 180°. With Ω = 0 the orbit inclination and the axial tilt are both rotations about X, so only the
        /// obliquity to the orbit enters, and it is neglected here (small-tilt approximation: off by under ±20 min at 30°).
        /// </summary>
        static double PhaseForLocalTime(double orbitAngleDeg, double padLongitude, double hour) =>
            (orbitAngleDeg + 180.0) - (12.0 - hour) * 15.0 - padLongitude;

        /// <summary>Teonan's orbit: Dp = 0.0858 AU, Da = 0.0908 AU, a = 0.0883 AU (= (Dp + Da)/2), e = 0.0283, i = 3.17°.</summary>
        static readonly double TeonanEccentricity = EccentricityFromApsides(0.0858, 0.0908);

        /// <summary>
        /// Teonan — innermost planet. Orbit: see <see cref="TeonanEccentricity"/>; the quoted period of 5.94 d is kept, Kepler
        /// with the star's mass gives 5.934 d (0.1 % shorter). Obliquity 0.6° to its orbit.
        /// </summary>
        static CelestialBodyPreset Teonan()
        {
            const double orbitAngle = 0.0, padLat = 12.0, padLon = 25.0;
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 1896325.0,
                mass = 1.24e23,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 54.1 * Day,
                axialTiltDegrees = TiltFromObliquity(PlanetInclination, 0.6),
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(TrueAnomalyDegrees(orbitAngle, TeonanEccentricity), padLon, StartHour),
                spaceBoundaryAltitude = 50000.0,
                atmosphere = new AtmosphereProperties { hasAtmosphere = false, surfaceDensity = 0.0, surfacePressure = 0.0, surfaceTemperature = 451.15, composition = "нет атмосферы" }
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            var g = new PlanetGenerationSettings
            {
                seed = 51031,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 1.4, continentBias = 0.05, continentAmplitude = 2200, oceanDepthAmplitude = 4500,
                warpStrength = 0.15,
                mountainFrequency = 3.0, mountainAmplitude = 2600, mountainOctaves = 7,
                detailFrequency = 120, detailAmplitude = 120, detailOctaves = 8,
                craterCount = 320, craterMinRadius = 1500, craterMaxRadius = 70000, craterDepthRatio = 0.09,
                lowland = new ColorRGB(0.30f, 0.28f, 0.26f),
                highland = new ColorRGB(0.50f, 0.47f, 0.43f),
                stain = new ColorRGB(0.38f, 0.33f, 0.29f), stainCoverage = 0.25, stainFrequency = 3.0,
                craterFloorDarkening = 0.3, moistureFrequency = 3.0,
                // The artists' texture of the Teonan model (FinalTeonan.fbx → teonan.jpg). Until the image is in
                // Resources/PlanetMaps/teonan the procedural colours above are used.
                albedoMap = "teonan",
                albedoMapDetail = 0.35
            };
            return new CelestialBodyPreset
            {
                Id = TeonanId, DisplayName = "Теонан (Teonan)",
                Description = "Ближайшая к звезде. Без атмосферы, силикатная серо-коричневая кора, железное ядро (ρ = 4350 кг/м³), кратеры и небольшие «моря», тектонически активна. Сутки = 54.1 земных.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = new OrbitProperties
                {
                    semiMajorAxis = 0.0883 * AU, eccentricity = TeonanEccentricity, inclinationDegrees = PlanetInclination,
                    period = 5.94 * Day, meanAnomalyAtEpochDegrees = orbitAngle
                },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 2 } // closest to the star, no air, no magnetosphere
            };
        }

        /// <summary>
        /// Quoris — the smallest planet, tidally locked. Orbit (2026-11-15): Dp = 0.124 AU, Da = 0.132 AU, a = 0.128 AU →
        /// e = 0.03125, i = 3.17°. The quoted period of 10.4 d (= the quoted rotation) is kept for both the orbit and the spin,
        /// so they stay exactly equal; Kepler with the star's mass gives 10.356 d (0.4 % shorter).
        /// Tidal lock on an inclined, eccentric orbit: the spin axis is the orbit normal (axial tilt = i, both about X with Ω = 0)
        /// and the prime meridian turns uniformly with the mean anomaly (phase = M₀ + 180°), so it points at the star at
        /// pericentre and apocentre and between them the star librates by up to ±(2e + 1.25e²) ≈ ±3.7° in longitude, as the
        /// Earth does in the Moon's sky. The pad's local time therefore swings by about ±15 min around 9:30.
        /// </summary>
        static CelestialBodyPreset Quoris()
        {
            const double orbitAngle = 130.0, padLat = 4.0;
            // Tidally locked: rotation period = orbital period, prime meridian faces the star on average (see above).
            // A pad 37.5° west of the sub-stellar point sees a (nearly) permanent 9:30 morning.
            double padLon = -(12.0 - StartHour) * 15.0;
            var a = new AtmosphereProperties
            {
                hasAtmosphere = true,
                surfacePressure = 100.0,           // very thin O₂ layer
                composition = "очень тонкий слой O₂",
                surfaceTemperature = 452.15,       // mean of −3 °C … 361 °C
                meanMolarMass = 0.032,
                coRotationFactor = 1.0
            };
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 1175730.0,
                mass = 2.82e22,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = QuorisPeriod,
                axialTiltDegrees = PlanetInclination, // spin axis = orbit normal
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0,
                spaceBoundaryAltitude = 100000.0,
                atmosphere = a
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            a.scaleHeight = AtmosphericModel.IsothermalScaleHeight(a.surfaceTemperature, a.meanMolarMass, p.SurfaceGravity);
            a.surfaceDensity = AtmosphericModel.IdealGasDensity(a.surfacePressure, a.meanMolarMass, a.surfaceTemperature);
            double ratio = a.surfaceDensity / 1.225;
            var optics = new AtmosphereOpticalProperties
            {
                rayleighScattering = new Vector3d(5.802e-6, 13.558e-6, 33.1e-6) * ratio,
                mieScattering = 0.0,
                autoShellHeight = false,
                shellHeight = 4.0 * a.scaleHeight
            };
            var g = new PlanetGenerationSettings
            {
                seed = 72244,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 1.2, continentAmplitude = 2000, oceanDepthAmplitude = 3000, warpStrength = 0.2,
                mountainFrequency = 4.5, mountainAmplitude = 3500,
                detailFrequency = 120, detailAmplitude = 100,
                craterCount = 140, craterMinRadius = 4000, craterMaxRadius = 110000, craterDepthRatio = 0.14,
                lowland = new ColorRGB(0.56f, 0.50f, 0.48f),
                highland = new ColorRGB(0.66f, 0.60f, 0.56f),
                stain = new ColorRGB(0.55f, 0.16f, 0.09f), stainCoverage = 0.32, stainFrequency = 5.0,
                craterFloorDarkening = 0.2,
                // "The heart": two deep overlapping craters with a smaller one below, near the morning terminator side.
                featureCraters = new[]
                {
                    new FeatureCrater { latitudeDegrees = -16.0, longitudeDegrees = -27.0, radius = 65000, depthRatio = 0.35 },
                    new FeatureCrater { latitudeDegrees = -16.0, longitudeDegrees = -21.0, radius = 65000, depthRatio = 0.35 },
                    new FeatureCrater { latitudeDegrees = -21.0, longitudeDegrees = -24.0, radius = 42000, depthRatio = 0.35 }
                }
            };
            return new CelestialBodyPreset
            {
                Id = QuorisId, DisplayName = "Кварис (Quoris)",
                Description = "Самая малая планета. Приливный захват: всегда повёрнута одной стороной к звезде (−3 °C … 361 °C). Очень тонкий кислородный слой, ржавые красные пятна, массивные глубокие кратеры и «сердечко».",
                Physical = p, Generation = g, Optics = optics,
                Orbit = new OrbitProperties
                {
                    semiMajorAxis = 0.128 * AU, eccentricity = EccentricityFromApsides(0.124, 0.132), inclinationDegrees = PlanetInclination,
                    period = QuorisPeriod, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true
                },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 1 }
            };
        }

        /// <summary>Quoris' orbital period = its rotation period [s] (designer data).</summary>
        const double QuorisPeriod = 10.4 * Day;

        /// <summary>Hermeous Mora's orbit: Dp = 0.411 AU, Da = 0.442 AU → e = 0.0363.</summary>
        static readonly double MoraEccentricity = EccentricityFromApsides(0.411, 0.442);

        /// <summary>
        /// Hermeous Mora. Orbit (2026-11-15): Dp = 0.411 AU, Da = 0.442 AU, a = 0.427 AU, i = 3.17°. (Dp + Da)/2 = 0.4265 AU; the
        /// quoted a = 0.427 AU is used because the star's mass is derived from it and the quoted 63.1 d (so a and T agree
        /// exactly by construction); the apsides it implies, 0.4115 / 0.4425 AU, are within 0.12 % of the quoted ones.
        /// Obliquity 2.6° to its orbit.
        /// </summary>
        static CelestialBodyPreset HermeousMora()
        {
            const double orbitAngle = 250.0, padLat = 18.0, padLon = -40.0;
            var a = new AtmosphereProperties
            {
                hasAtmosphere = true,
                surfacePressure = 7.5e6,           // lethal, Venus-like
                composition = "SO₂ и CO₂ с примесью O₂ и Ar; сернокислотные облака",
                surfaceTemperature = 573.15,       // 300 °C
                meanMolarMass = 0.052,             // SO₂ + CO₂ with some O₂ and Ar
                coRotationFactor = 1.0
            };
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 8209166.0,
                mass = 1.46e25,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 4.65 * Day,
                axialTiltDegrees = TiltFromObliquity(PlanetInclination, 2.6),
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(TrueAnomalyDegrees(orbitAngle, MoraEccentricity), padLon, StartHour),
                spaceBoundaryAltitude = 120000.0,
                atmosphere = a
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            a.scaleHeight = AtmosphericModel.IsothermalScaleHeight(a.surfaceTemperature, a.meanMolarMass, p.SurfaceGravity);
            a.surfaceDensity = AtmosphericModel.IdealGasDensity(a.surfacePressure, a.meanMolarMass, a.surfaceTemperature);
            var optics = new AtmosphereOpticalProperties
            {
                // Sulphur aerosols scatter red/yellow more than blue → cream-yellow disk, and the light that is left
                // after a long path is bluish → whitish-blue sunsets. Blue organic haze high above (blue limb).
                rayleighScattering = new Vector3d(1.8e-4, 1.5e-4, 0.9e-4),
                mieScattering = 3.0e-5, mieExtinctionRatio = 1.3, mieScaleHeight = 3500.0, mieAnisotropy = 0.7,
                hazeScattering = new Vector3d(0.6e-6, 1.6e-6, 4.5e-6), hazeAltitude = 75000.0, hazeThickness = 7000.0
            };
            var g = new PlanetGenerationSettings
            {
                seed = 33157,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 0.9, continentAmplitude = 3500, oceanDepthAmplitude = 4000,
                warpFrequency = 1.2, warpStrength = 0.38,
                mountainFrequency = 3.5, mountainAmplitude = 3800,
                detailFrequency = 160, detailAmplitude = 150,
                craterCount = 12, craterMinRadius = 5000, craterMaxRadius = 60000, craterDepthRatio = 0.05,
                cloudCoverage = 0.97, cloudTint = new ColorRGB(0.95f, 0.86f, 0.62f), // an unbroken deck of sulphuric acid
                lowland = new ColorRGB(0.66f, 0.45f, 0.22f),
                highland = new ColorRGB(0.86f, 0.77f, 0.58f),
                stain = new ColorRGB(0.40f, 0.22f, 0.12f), stainCoverage = 0.12, stainFrequency = 9.0,
                craterFloorDarkening = 0.15, moistureFrequency = 2.0
            };
            return new CelestialBodyPreset
            {
                Id = HermeousMoraId, DisplayName = "Гермеус Мора (Hermeous Mora)",
                Description = "300 °C, смертельное давление, сернокислотные осадки, атмосфера из SO₂ и CO₂ с примесью O₂ и Ar. Голубая органическая дымка над атмосферой, бело-голубые закаты. g ≈ 14.5 м/с². Сутки = 4.65 земных. Один несферический спутник на вытянутой орбите 18 000 … 37 000 км.",
                Physical = p, Generation = g, Optics = optics,
                Orbit = new OrbitProperties
                {
                    semiMajorAxis = 0.427 * AU, eccentricity = MoraEccentricity, inclinationDegrees = PlanetInclination,
                    period = 63.1 * Day, meanAnomalyAtEpochDegrees = orbitAngle
                },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 1, acidLevel = 1 } // sulphuric acid rain inside the air
            };
        }

        /// <summary>
        /// Aegis — Hermeous Mora's irregular moon (size and density are estimates). Orbit (2026-11-15): Dp = 18 280 005 m,
        /// Da = 37 311 552 m, a = 27 795 780 m (= (Dp + Da)/2 within 2 m) → e = 0.342, i = 4.37° (to the reference plane).
        /// No period was given: Kepler with Mora's μ gives 0.341 d (8.2 h). The pericentre is 2.23 Mora radii from its centre
        /// (≈ 10 000 km above the surface) — inside the fluid Roche limit for ρ = 2100 kg/m³ (≈ 3.5 R), which a small
        /// solid, non-spherical body survives (rigid limit ≈ 1.8 R).
        /// Kept tidally locked (rotation = revolution, prime meridian on the mean direction to Mora); with e = 0.34 Mora then
        /// librates by up to ≈ ±40° in its sky — a real 1:1 lock this eccentric would be fragile (cf. Mercury's 3:2), accepted.
        /// </summary>
        static CelestialBodyPreset MoraMoon(double parentMu)
        {
            const double orbitAngle = 40.0, radius = 120000.0, density = 2100.0, inclination = 4.37;
            var orbit = new OrbitProperties
            {
                semiMajorAxis = 27795780.0, eccentricity = EccentricityFromApsides(18280005.0, 37311552.0),
                inclinationDegrees = inclination, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true
            };
            double period = OrbitalMechanics.CircularPeriod(parentMu, orbit.semiMajorAxis);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = radius,
                mass = density * 4.0 / 3.0 * Math.PI * radius * radius * radius,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = period,
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0, // mean direction to Mora
                axialTiltDegrees = inclination,                   // spin axis = orbit normal
                spaceBoundaryAltitude = 5000.0,
                atmosphere = new AtmosphereProperties { hasAtmosphere = false, surfaceDensity = 0.0, surfacePressure = 0.0, composition = "нет атмосферы" }
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            // Non-spherical: relief amplitudes of ~15 % of the radius at low frequency make a potato-shaped body.
            var g = new PlanetGenerationSettings
            {
                seed = 90417,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 0.7, continentOctaves = 4, continentAmplitude = 24000, oceanDepthAmplitude = 24000,
                warpStrength = 0.35,
                mountainFrequency = 3.0, mountainAmplitude = 2500,
                detailFrequency = 40, detailAmplitude = 200,
                craterCount = 70, craterMinRadius = 1500, craterMaxRadius = 30000, craterDepthRatio = 0.16,
                lowland = new ColorRGB(0.55f, 0.48f, 0.32f),
                highland = new ColorRGB(0.70f, 0.63f, 0.45f),
                craterFloorDarkening = 0.3
            };
            return new CelestialBodyPreset
            {
                Id = MoraMoonId, DisplayName = "Эгида (Aegis)", ParentId = HermeousMoraId,
                Description = "Несферический спутник Гермеус Моры на вытянутой орбите 18 000 … 37 000 км (обращение ≈ 8 ч), приливно захвачен. Радиус ≈ 120 км (параметры не заданы — оценка).",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = orbit,
                LaunchLatitude = 0.0, LaunchLongitude = 90.0,
                Hazards = new EnvironmentHazards { radiationLevel = 1 }
            };
        }
    }
}
