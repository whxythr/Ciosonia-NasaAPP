using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Procedural;
using SpaceSim.Units;

namespace SpaceSim.Planets
{
    /// <summary>
    /// The double planet Mocha-Mocha + Dott — the outermost bodies of the system (designer data of 2026-11-15).
    ///
    /// Given:
    /// • barycentre orbit around the star: Dp = 4.23 AU, Da = 4.50 AU, a = 4.36 AU, i = 5.46°;
    /// • Dott: M = 7.95·10²² kg, R = 1 644 016 m, ρ = 4270 kg/m³, T = −202 °C, rotation 5.39 d, g = 1.96 m/s², ≈ 4100 km from the
    ///   barycentre, tilt 2.1°;
    /// • Mocha-Mocha: M = 1.65·10²³ kg, R = 2 034 254 m, ρ = 4680 kg/m³, T = −249 °C, rotation 26.0 d, g = 2.66 m/s², ≈ 970 km from
    ///   the barycentre (the barycentre inside Mocha-Mocha), the same 2.1° tilt.
    /// M and R reproduce the quoted ρ (4271 / 4679 kg/m³) and g (1.963 / 2.661 m/s²) within 0.1 %; they are kept as given.
    ///
    /// What had to change, and why:
    /// • The two barycentre distances contradict the masses: around a common barycentre r_Mocha / r_Dott = M_Dott / M_Mocha = 0.482,
    ///   but 970 / 4100 = 0.237. Their sum, 5070 km, is barely more than the sum of the radii (3678 km): the surfaces would be
    ///   1400 km apart, right at the fluid Roche limit (2.44·R_Mocha·(ρ_Mocha/ρ_Dott)^⅓ ≈ 5120 km), with a 4.9-hour orbit — and
    ///   both bodies would have been despun to that period long ago, while the data gives 5.39 d and 26 d. Patched conics cannot
    ///   fly such a pair either: Dott's sphere of influence (≥ 3 of its radii = 4930 km) would reach under Mocha-Mocha's surface.
    /// • Chosen instead: Dott is tidally locked, like Charon, so its quoted rotation of 5.39 d is also its orbital period, and
    ///   the separation follows from Kepler's third law for the pair: d = (G(M₁ + M₂)·T²/4π²)^⅓ = 44 755 km (≈ 22 Mocha radii,
    ///   Pluto–Charon: 16 Pluto radii). Mocha-Mocha keeps its slower 26.0 d spin (the heavier body despins more slowly; not yet
    ///   synchronous). The barycentre then lies 14 552 km from Mocha-Mocha's centre and 30 203 km from Dott's (exactly the
    ///   mass ratio) — OUTSIDE Mocha-Mocha, as for Pluto–Charon, which is what makes it a true double planet. So the quoted
    ///   970 / 4100 km and "the barycentre inside Mocha-Mocha" are NOT reproduced; the rotation periods, masses and radii are.
    /// • The mutual orbit is circular (e not given) and inclined 2.1° to the reference plane (like every inclination in the
    ///   catalog); both spin axes are its normal (axial tilt 2.1°), i.e. 3.4° from the barycentre's orbit.
    ///
    /// How it is modelled in patched conics: Mocha-Mocha is the primary on the star's rails, Dott its satellite.
    /// • A massless barycentre body as the parent was rejected: every catalog entry is a full body (surface, generation, map
    ///   label, science, pads), and a vehicle near either planet would have to switch between three overlapping spheres.
    /// • Mocha-Mocha's rails are the barycentre's orbit (a = 4.36 AU, e = (4.50 − 4.23)/(4.50 + 4.23) = 0.0309, i = 5.46°,
    ///   T from Kepler with the star: 2059 d ≈ 5.64 years — no period was given; (Dp + Da)/2 = 4.365 AU, the quoted 4.36 is used).
    ///   Its own 14 552 km wobble about the barycentre is not modelled: 2·10⁻⁵ of its distance from the star.
    /// • Dott's rails are the RELATIVE orbit (Dott about Mocha-Mocha, a = d) with the explicit period of 5.39 d, so the rails'
    ///   implied μ = 4π²d³/T² is G(M₁ + M₂) — the exact two-body relative motion — while a vehicle inside Mocha-Mocha's sphere
    ///   feels only G·M₁ (the usual patched-conic approximation, as the Moon's pull on the Earth's frame is ignored elsewhere).
    ///   Dott's sphere of influence, d·(M₂/M₁)^0.4 = 33 400 km, ends 11 300 km (5.6 R) from Mocha-Mocha's centre.
    ///
    /// Not given, chosen: colours (Mocha-Mocha coffee-brown — after its name — with pale frost; Dott grey), relief, pads,
    /// no atmosphere. The artist maps Resources/PlanetMaps/mocha-mocha and /dott do not exist yet: procedural colours are used
    /// until they do. Radiation level 1 (airless, no magnetosphere; far from the star).
    /// Temperatures are kept as given, although at 4.36 AU from this star radiative equilibrium is ≈ 84…149 K (albedo 0.9…0):
    /// Dott's 71 K needs a very bright surface, Mocha-Mocha's 24 K is far colder than sunlight allows (≈ −249 °C would need
    /// albedo ≈ 0.9999) — flagged for the designer.
    /// </summary>
    public static partial class HomeSystemCatalog
    {
        /// <summary>The larger body of the double planet; carries the barycentre's orbit around the star.</summary>
        public const string MochaMochaId = "mocha-mocha";
        /// <summary>The smaller body of the double planet, tidally locked to Mocha-Mocha.</summary>
        public const string DottId = "dott";

        /// <summary>Inclination of the double planet's barycentre orbit [deg] (designer data).</summary>
        public const double DoublePlanetInclination = 5.46;
        /// <summary>Inclination of the mutual Mocha-Mocha–Dott orbit and of both spin axes [deg] (designer data).</summary>
        public const double DoublePlanetMutualInclination = 2.1;

        public const double MochaMochaMass = 1.65e23, DottMass = 7.95e22;
        /// <summary>Dott's rotation = its orbital period around Mocha-Mocha [s] (tidally locked).</summary>
        public const double DottPeriod = 5.39 * Day;

        /// <summary>Mocha-Mocha–Dott separation [m] from Kepler's third law for the pair (≈ 44 755 km).</summary>
        public static double DoublePlanetSeparation =>
            Math.Pow(PhysicalConstants.GravitationalConstant * (MochaMochaMass + DottMass) * DottPeriod * DottPeriod / (4.0 * Math.PI * Math.PI), 1.0 / 3.0);

        /// <summary>Distance of Mocha-Mocha's centre from the barycentre [m] (≈ 14 552 km).</summary>
        public static double MochaMochaBarycentreDistance => DoublePlanetSeparation * DottMass / (MochaMochaMass + DottMass);

        /// <summary>Distance of Dott's centre from the barycentre [m] (≈ 30 203 km).</summary>
        public static double DottBarycentreDistance => DoublePlanetSeparation * MochaMochaMass / (MochaMochaMass + DottMass);

        /// <summary>Position of the double planet on its orbit at t = 0 [deg] (mean anomaly).</summary>
        const double DoublePlanetOrbitAngle = 160.0;
        static readonly double DoublePlanetEccentricity = EccentricityFromApsides(4.23, 4.50);

        static void AddDoublePlanet(List<CelestialBodyPreset> list)
        {
            list.Add(MochaMocha());
            list.Add(Dott());
        }

        static CelestialBodyPreset MochaMocha()
        {
            const double padLat = 15.0, padLon = 30.0;
            double sunAngle = TrueAnomalyDegrees(DoublePlanetOrbitAngle, DoublePlanetEccentricity);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 2034254.0,
                mass = MochaMochaMass,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 26.0 * Day,
                axialTiltDegrees = DoublePlanetMutualInclination, // spin axis = normal of the mutual orbit
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(sunAngle, padLon, StartHour),
                spaceBoundaryAltitude = 15000.0,
                atmosphere = new AtmosphereProperties
                {
                    hasAtmosphere = false, surfaceDensity = 0.0, surfacePressure = 0.0,
                    surfaceTemperature = 24.15, // −249 °C (designer data, see the class notes)
                    composition = "нет атмосферы"
                }
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            var g = new PlanetGenerationSettings
            {
                seed = 46811,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 1.1, continentAmplitude = 2200, oceanDepthAmplitude = 2600, warpStrength = 0.2,
                mountainFrequency = 3.0, mountainAmplitude = 1800,
                detailFrequency = 120, detailAmplitude = 90,
                craterCount = 380, craterMinRadius = 800, craterMaxRadius = 60000, craterDepthRatio = 0.11,
                lowland = new ColorRGB(0.30f, 0.20f, 0.13f),     // dark coffee basins
                highland = new ColorRGB(0.55f, 0.42f, 0.30f),    // milky-coffee uplands
                stain = new ColorRGB(0.80f, 0.77f, 0.72f), stainCoverage = 0.18, stainFrequency = 3.5, // pale frost patches
                craterFloorDarkening = 0.2, moistureFrequency = 2.5,
                albedoMap = "mocha-mocha", // not drawn yet: procedural colours until Resources/PlanetMaps/mocha-mocha exists
                albedoMapDetail = 0.3
            };
            return new CelestialBodyPreset
            {
                Id = MochaMochaId, DisplayName = "Моча-Моча (Mocha-Mocha)",
                Description = "Самая дальняя планета, больший компонент двойной планеты Моча-Моча + Дот. Кофейно-коричневая, без атмосферы, в кратерах и пятнах инея. Общий центр масс пары лежит вне планеты, в 14 550 км от её центра; пара обращается вокруг звезды за 5.6 года. 1.65·10²³ кг, g = 2.66 м/с², −249 °C, сутки 26 земных.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = new OrbitProperties
                {
                    // The barycentre's orbit (see the class notes); period 0 = Kepler with the star's μ.
                    semiMajorAxis = 4.36 * AU, eccentricity = DoublePlanetEccentricity, inclinationDegrees = DoublePlanetInclination,
                    meanAnomalyAtEpochDegrees = DoublePlanetOrbitAngle
                },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 1 }
            };
        }

        static CelestialBodyPreset Dott()
        {
            const double orbitAngle = 200.0, padLat = 5.0;
            // Prime meridian faces Mocha-Mocha (direction angle + 180°, circular orbit so mean = true anomaly); the star is where
            // it is seen from Mocha-Mocha, so the sub-solar longitude is sunAngle − orbitAngle; the pad is 30° west of it (≈ 10:00).
            double sunAngle = TrueAnomalyDegrees(DoublePlanetOrbitAngle, DoublePlanetEccentricity);
            double padLon = Wrap180(sunAngle - orbitAngle - 30.0);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 1644016.0,
                mass = DottMass,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = DottPeriod,
                axialTiltDegrees = DoublePlanetMutualInclination, // spin axis = orbit normal
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0,
                spaceBoundaryAltitude = 12000.0,
                atmosphere = new AtmosphereProperties
                {
                    hasAtmosphere = false, surfaceDensity = 0.0, surfacePressure = 0.0,
                    surfaceTemperature = 71.15, // −202 °C
                    composition = "нет атмосферы"
                }
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            var g = new PlanetGenerationSettings
            {
                seed = 58127,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 1.3, continentAmplitude = 1800, oceanDepthAmplitude = 2200, warpStrength = 0.15,
                mountainFrequency = 3.5, mountainAmplitude = 1500,
                detailFrequency = 130, detailAmplitude = 80,
                craterCount = 450, craterMinRadius = 600, craterMaxRadius = 45000, craterDepthRatio = 0.12,
                lowland = new ColorRGB(0.40f, 0.40f, 0.42f),
                highland = new ColorRGB(0.66f, 0.66f, 0.68f),
                stain = new ColorRGB(0.86f, 0.87f, 0.90f), stainCoverage = 0.2, stainFrequency = 4.0, // bright frost
                craterFloorDarkening = 0.2, moistureFrequency = 3.0,
                albedoMap = "dott", // not drawn yet: procedural colours until Resources/PlanetMaps/dott exists
                albedoMapDetail = 0.3
            };
            return new CelestialBodyPreset
            {
                Id = DottId, DisplayName = "Дот (Dott)", ParentId = MochaMochaId,
                Description = "Меньший компонент двойной планеты Моча-Моча + Дот. Серый, без атмосферы, в кратерах и светлом инее. Приливно захвачен: всегда повёрнут к Моче-Моче одной стороной, сутки = обращение = 5.39 земных, расстояние между центрами ≈ 44 750 км. 7.95·10²² кг, g = 1.96 м/с², −202 °C.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = new OrbitProperties
                {
                    // Relative orbit about Mocha-Mocha; the explicit period makes the rails' μ equal G(M₁ + M₂).
                    semiMajorAxis = DoublePlanetSeparation, inclinationDegrees = DoublePlanetMutualInclination,
                    period = DottPeriod, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true
                },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 1 }
            };
        }
    }
}
