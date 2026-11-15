using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Procedural;

namespace SpaceSim.Planets
{
    /// <summary>
    /// The two moons of Tholin: Lin and Tiafoe. The orbits are designer data (2026-11-15); size and density are ESTIMATES,
    /// chosen to look like the moons of a Mars-analogue planet (small, airless, captured or accreted):
    /// • Tiafoe — the inner, small irregular one: Dp = 19 173 309 m, Da = 29 635 390 m, a = 24 405 443 m (≈ 3.4 R of Tholin)
    ///   → e = 0.214, i = 40.4°; Kepler with Tholin's μ: 0.351 d (8.4 h). (Dp + Da)/2 = 24 404 350 m, 1.1 km (4·10⁻⁵) below the
    ///   quoted a, which is used. R = 95 km, ρ = 1900 kg/m³, tidally locked, reddish-brown (tholin dust), lumpy like Aegis.
    ///   Its pericentre (2.65 R of Tholin) is inside the fluid Roche limit for ρ = 1900 kg/m³ (2.44·R·(5880/1900)^⅓ ≈ 3.6 R) but
    ///   outside the rigid one (≈ 1.8 R): it must hold together by material strength — accepted (the size is only an estimate).
    ///   The steep 40° orbit suggests a captured body.
    /// • Lin — the outer, larger one: Dp = 44 991 524 m, Da = 52 279 669 m, a = 48 635 612 m (≈ 6.7 R) → e = 0.0749, i = 1.26°;
    ///   Kepler: 0.987 d. (Dp + Da)/2 is 16 m below the quoted a. R = 310 km, ρ = 2600 kg/m³ (rock with some ice), tidally
    ///   locked, dark grey-brown, heavily cratered.
    /// (Before 2026-11-15 the estimates were circular orbits with Lin inside at 62 000 km and Tiafoe outside at 165 000 km.)
    /// Inclinations are to the reference plane, like every orbit; each locked moon's spin axis is its orbit normal. Both are far
    /// inside Tholin's Hill sphere (r_H = a·(m / 3M★)^⅓ ≈ 1.1·10⁹ m), and their orbits never cross (Tiafoe's apocentre 29.6 Mm,
    /// Lin's pericentre 45.0 Mm).
    /// </summary>
    public static partial class HomeSystemCatalog
    {
        /// <summary>Outer, larger moon of Tholin.</summary>
        public const string LinId = "lin";
        /// <summary>Inner, small irregular moon of Tholin on a steep (40°) orbit.</summary>
        public const string TiafoeId = "tiafoe";

        static void AddTholinMoons(List<CelestialBodyPreset> list, double tholinMu)
        {
            list.Add(Lin(tholinMu));
            list.Add(Tiafoe(tholinMu));
        }

        static CelestialBodyPreset Lin(double parentMu)
        {
            const double orbitAngle = 130.0, radius = 310000.0, density = 2600.0, inclination = 1.26;
            var orbit = new OrbitProperties
            {
                semiMajorAxis = 48635612.0, eccentricity = EccentricityFromApsides(44991524.0, 52279669.0),
                inclinationDegrees = inclination, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true
            };
            double period = OrbitalMechanics.CircularPeriod(parentMu, orbit.semiMajorAxis);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = radius,
                mass = density * 4.0 / 3.0 * Math.PI * radius * radius * radius,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = period,
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0, // prime meridian faces Tholin (mean direction)
                axialTiltDegrees = inclination,                   // spin axis = orbit normal
                spaceBoundaryAltitude = 8000.0,
                atmosphere = new AtmosphereProperties
                {
                    hasAtmosphere = false, surfaceDensity = 0.0, surfacePressure = 0.0,
                    surfaceTemperature = 205.0, // estimate: dark airless body at 0.865 AU from a K star
                    composition = "нет атмосферы"
                }
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            var g = new PlanetGenerationSettings
            {
                seed = 71203,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 1.2, continentAmplitude = 1800, oceanDepthAmplitude = 2200, warpStrength = 0.25,
                mountainFrequency = 3.5, mountainAmplitude = 1400,
                detailFrequency = 110, detailAmplitude = 90,
                craterCount = 420, craterMinRadius = 400, craterMaxRadius = 22000, craterDepthRatio = 0.13,
                lowland = new ColorRGB(0.27f, 0.24f, 0.22f),
                highland = new ColorRGB(0.42f, 0.37f, 0.33f),
                stain = new ColorRGB(0.20f, 0.18f, 0.17f), stainCoverage = 0.22, stainFrequency = 3.0,
                craterFloorDarkening = 0.25, moistureFrequency = 3.0
            };
            return new CelestialBodyPreset
            {
                Id = LinId, DisplayName = "Лин (Lin)", ParentId = TholinId,
                Description = "Внешний и крупнейший спутник Золина. Тёмный серо-коричневый, без атмосферы, весь в кратерах, приливно захвачен. Орбита 45 000 … 52 000 км, обращение ≈ 24 ч. Размер не задан — оценка: R ≈ 310 км, ρ ≈ 2600 кг/м³.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = orbit,
                LaunchLatitude = 6.0, LaunchLongitude = -20.0, // near side: Tholin hangs in the sky
                Hazards = new EnvironmentHazards { radiationLevel = 1 }
            };
        }

        static CelestialBodyPreset Tiafoe(double parentMu)
        {
            const double orbitAngle = 290.0, radius = 95000.0, density = 1900.0, inclination = 40.4;
            var orbit = new OrbitProperties
            {
                semiMajorAxis = 24405443.0, eccentricity = EccentricityFromApsides(19173309.0, 29635390.0),
                inclinationDegrees = inclination, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true
            };
            double period = OrbitalMechanics.CircularPeriod(parentMu, orbit.semiMajorAxis);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = radius,
                mass = density * 4.0 / 3.0 * Math.PI * radius * radius * radius,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = period,
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0, // mean direction to Tholin
                axialTiltDegrees = inclination,                   // spin axis = orbit normal
                spaceBoundaryAltitude = 4000.0,
                atmosphere = new AtmosphereProperties
                {
                    hasAtmosphere = false, surfaceDensity = 0.0, surfacePressure = 0.0,
                    surfaceTemperature = 210.0, // estimate
                    composition = "нет атмосферы"
                }
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            // Irregular: low-frequency relief of ~15 % of the radius makes a lumpy body (as Mora's moon Aegis).
            var g = new PlanetGenerationSettings
            {
                seed = 94411,
                surfaceStyle = SurfaceStyle.Rocky,
                hasOcean = false,
                continentFrequency = 0.8, continentOctaves = 4, continentAmplitude = 14000, oceanDepthAmplitude = 14000,
                warpStrength = 0.35,
                mountainFrequency = 3.0, mountainAmplitude = 1500,
                detailFrequency = 45, detailAmplitude = 150,
                craterCount = 90, craterMinRadius = 800, craterMaxRadius = 18000, craterDepthRatio = 0.16,
                lowland = new ColorRGB(0.42f, 0.25f, 0.17f),
                highland = new ColorRGB(0.58f, 0.38f, 0.26f),
                stain = new ColorRGB(0.66f, 0.30f, 0.16f), stainCoverage = 0.2, stainFrequency = 4.0,
                craterFloorDarkening = 0.25
            };
            return new CelestialBodyPreset
            {
                Id = TiafoeId, DisplayName = "Тиафо (Tiafoe)", ParentId = TholinId,
                Description = "Внутренний малый спутник Золина неправильной формы, рыхлая груда обломков, покрытая красноватой толиновой пылью с планеты. Приливно захвачен. Сильно наклонённая (40°) вытянутая орбита 19 000 … 30 000 км, обращение ≈ 8.4 ч. Размер не задан — оценка: R ≈ 95 км, ρ ≈ 1900 кг/м³.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = orbit,
                LaunchLatitude = 0.0, LaunchLongitude = 30.0,
                Hazards = new EnvironmentHazards { radiationLevel = 1 }
            };
        }
    }
}
