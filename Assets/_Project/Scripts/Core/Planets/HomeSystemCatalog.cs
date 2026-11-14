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
    /// • Orbits are circular and coplanar (no e, i given).
    /// • The star's mass is not given; it is derived from Kepler's third law, M = 4π²a³/(G·T²), using the
    ///   given a and T of Hermeous Mora (≈ 5.19·10³⁰ kg ≈ 2.6 M☉). All three planets agree within 1 %.
    /// </summary>
    public static class HomeSystemCatalog
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

        public static double StarMassFromKepler(double semiMajorAxis, double period) =>
            4.0 * Math.PI * Math.PI * Math.Pow(semiMajorAxis, 3) / (PhysicalConstants.GravitationalConstant * period * period);

        public static StarProperties Star() => new StarProperties
        {
            name = "Звезда",
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
            return list;
        }

        /// <summary>
        /// Kolimpri — inner moon of the start planet (designer data: M = 1.43·10²¹ kg, R = 427 406 m, ρ = 4440 kg/m³,
        /// g = 0.531 m/s², a = 56 896 095 m, rotation = revolution = 1.7 d, −111…−156 °C).
        /// The given M and R give ρ = 4373 kg/m³ and g = 0.522 m/s² (1.5–2 % below the quoted values); the mass is kept.
        /// The period follows from Kepler with the start planet's μ: 1.703 d (the quoted 1.7 d agrees).
        /// Tidally locked: the map's central meridian (longitude 0) always faces the planet.
        /// Colours come from the artist map Resources/PlanetMaps/kolimpri (iron-rich pink highlands, dark grey "seas");
        /// without it the procedural colours below approximate the map.
        /// </summary>
        static CelestialBodyPreset Kolimpri(double parentMu)
        {
            // 60° from the anti-star direction of the planet (home orbit angle 40° + 0°): the near side and the pad
            // (≈ 35° from the sub-solar point) are in the afternoon sun at the start; from the planet it is gibbous.
            const double orbitAngle = 100.0, radius = 427406.0;
            var orbit = new OrbitProperties { semiMajorAxis = 56896095.0, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true };
            double period = OrbitalMechanics.CircularPeriod(parentMu, orbit.semiMajorAxis);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = radius,
                mass = 1.43e21,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = period,
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0,  // prime meridian faces the parent
                axialTiltDegrees = 0.0,
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
        /// </summary>
        static CelestialBodyPreset Tromos(double parentMu)
        {
            const double orbitAngle = 75.0, radius = 266478.0, padLon = 40.0;
            var orbit = new OrbitProperties { semiMajorAxis = 97059000.0, meanAnomalyAtEpochDegrees = orbitAngle };
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
                // Seen from a moon the star is where it is seen from the planet (orbit angle 40° + 180°): pad at 10:00.
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(HomeOrbitAngle, padLon, 10.0),
                axialTiltDegrees = 0.0,
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
        /// </summary>
        static CelestialBodyPreset HomeWorld()
        {
            const double orbitAngle = HomeOrbitAngle, padLat = 28.0, padLon = -52.0;
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
                axialTiltDegrees = 22.5,
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(orbitAngle, padLon, StartHour),
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
                deepOcean = new ColorRGB(0.07f, 0.02f, 0.16f),      // violet seas
                shallowOcean = new ColorRGB(0.24f, 0.10f, 0.36f),
                seaIce = new ColorRGB(0.82f, 0.82f, 0.90f),
                grassland = new ColorRGB(0.26f, 0.42f, 0.14f),
                forest = new ColorRGB(0.10f, 0.26f, 0.08f),
                desert = new ColorRGB(0.80f, 0.64f, 0.40f),
                craterCount = 6
            };
            return new CelestialBodyPreset
            {
                Id = HomeWorldId, DisplayName = "Четвёртая планета (старт)",
                Description = "Обитаемая стартовая планета: копия Земли с меньшей массой (0.842 M⊕) и g = 8.27 м/с². Фиолетовая вода (свет звезды K-класса), экваториальные пустыни, горы у экваториальных побережий, на полюсах до −49 °C. Сплюснутость 0.286 %, наклон оси 22.5°, магнитный полюс 11.5°, B = 0.254 Гс, магнитопауза 13 627 603 м. Сутки 0.844 земных, год 124 дня.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = new OrbitProperties { semiMajorAxis = 0.67 * AU, period = 124.0 * Day, meanAnomalyAtEpochDegrees = orbitAngle },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards() // magnetosphere + atmosphere: safe
            };
        }

        /// <summary>
        /// Rotation phase at t = 0 that puts a pad at <paramref name="padLongitude"/> at local solar time
        /// <paramref name="hour"/> (circular coplanar orbit, small tilt). The Sun is at orbit angle + 180°.
        /// </summary>
        static double PhaseForLocalTime(double orbitAngleDeg, double padLongitude, double hour) =>
            (orbitAngleDeg + 180.0) - (12.0 - hour) * 15.0 - padLongitude;

        static CelestialBodyPreset Teonan()
        {
            const double orbitAngle = 0.0, padLat = 12.0, padLon = 25.0;
            var p = new PlanetPhysicalProperties
            {
                meanRadius = 1896325.0,
                mass = 1.24e23,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = 54.1 * Day,
                axialTiltDegrees = 0.6,
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(orbitAngle, padLon, StartHour),
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
                Orbit = new OrbitProperties { semiMajorAxis = 0.0883 * AU, period = 5.94 * Day, meanAnomalyAtEpochDegrees = orbitAngle },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 2 } // closest to the star, no air, no magnetosphere
            };
        }

        static CelestialBodyPreset Quoris()
        {
            const double orbitAngle = 130.0, padLat = 4.0;
            // Tidally locked: rotation period = orbital period, prime meridian always faces the star.
            // A pad 37.5° west of the sub-stellar point sees permanent 9:30 morning.
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
                siderealRotationPeriod = 10.4 * Day,
                axialTiltDegrees = 0.0,
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
                Orbit = new OrbitProperties { semiMajorAxis = 0.128 * AU, period = 10.4 * Day, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 1 }
            };
        }

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
                axialTiltDegrees = 2.6,
                rotationPhaseAtEpochDegrees = PhaseForLocalTime(orbitAngle, padLon, StartHour),
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
                Description = "300 °C, смертельное давление, сернокислотные осадки, атмосфера из SO₂ и CO₂ с примесью O₂ и Ar. Голубая органическая дымка над атмосферой, бело-голубые закаты. g ≈ 14.5 м/с². Сутки = 4.65 земных. Один несферический спутник на 30 000 км.",
                Physical = p, Generation = g, Optics = optics,
                Orbit = new OrbitProperties { semiMajorAxis = 0.427 * AU, period = 63.1 * Day, meanAnomalyAtEpochDegrees = orbitAngle },
                LaunchLatitude = padLat, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = 1, acidLevel = 1 } // sulphuric acid rain inside the air
            };
        }

        static CelestialBodyPreset MoraMoon(double parentMu)
        {
            const double orbitAngle = 40.0, radius = 120000.0, density = 2100.0;
            var orbit = new OrbitProperties { semiMajorAxis = 3.0e7, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true };
            double period = OrbitalMechanics.CircularPeriod(parentMu, orbit.semiMajorAxis);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = radius,
                mass = density * 4.0 / 3.0 * Math.PI * radius * radius * radius,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = period,
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0,
                axialTiltDegrees = 0.0,
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
                Id = MoraMoonId, DisplayName = "Мора I (спутник)", ParentId = HermeousMoraId,
                Description = "Несферический спутник Гермеус Моры на расстоянии 30 000 км, приливно захвачен. Радиус ≈ 120 км (параметры не заданы — оценка).",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = orbit,
                LaunchLatitude = 0.0, LaunchLongitude = 90.0,
                Hazards = new EnvironmentHazards { radiationLevel = 1 }
            };
        }
    }
}
