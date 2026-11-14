using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Procedural;

namespace SpaceSim.Planets
{
    /// <summary>
    /// The two moons of Tholin: Lin and Tiafoe. Only the names were given — every number here is an ESTIMATE, chosen to be
    /// physically consistent and to look like the moons of a Mars-analogue planet (small, airless, captured or accreted rubble):
    /// • Lin — the inner, larger one: R = 310 km, ρ = 2600 kg/m³ (rock with some ice), a = 62 000 km (≈ 8.6 R of Tholin),
    ///   tidally locked, dark grey-brown, heavily cratered.
    /// • Tiafoe — the outer, small irregular one: R = 95 km, ρ = 1900 kg/m³ (porous rubble pile), a = 165 000 km (≈ 23 R),
    ///   tidally locked, reddish-brown (tholin dust from the planet), lumpy like Mora's moon.
    /// Both are far inside Tholin's Hill sphere (r_H = a·(m / 3M★)^⅓ ≈ 1.1·10⁹ m) and orbit in Tholin's equatorial plane
    /// (the simulation's orbits are coplanar). Periods follow from Kepler's third law with Tholin's μ.
    /// </summary>
    public static partial class HomeSystemCatalog
    {
        /// <summary>Inner moon of Tholin (estimated data).</summary>
        public const string LinId = "lin";
        /// <summary>Outer moon of Tholin (estimated data).</summary>
        public const string TiafoeId = "tiafoe";

        static void AddTholinMoons(List<CelestialBodyPreset> list, double tholinMu)
        {
            list.Add(Lin(tholinMu));
            list.Add(Tiafoe(tholinMu));
        }

        static CelestialBodyPreset Lin(double parentMu)
        {
            const double orbitAngle = 130.0, radius = 310000.0, density = 2600.0;
            var orbit = new OrbitProperties { semiMajorAxis = 6.2e7, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true };
            double period = OrbitalMechanics.CircularPeriod(parentMu, orbit.semiMajorAxis);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = radius,
                mass = density * 4.0 / 3.0 * Math.PI * radius * radius * radius,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = period,
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0, // prime meridian faces Tholin
                axialTiltDegrees = 0.0,
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
                Description = "Внутренний и крупнейший спутник Золина. Тёмный серо-коричневый, без атмосферы, весь в кратерах, приливно захвачен. Параметры не заданы — оценка: R ≈ 310 км, ρ ≈ 2600 кг/м³, орбита ≈ 62 000 км.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = orbit,
                LaunchLatitude = 6.0, LaunchLongitude = -20.0, // near side: Tholin hangs in the sky
                Hazards = new EnvironmentHazards { radiationLevel = 1 }
            };
        }

        static CelestialBodyPreset Tiafoe(double parentMu)
        {
            const double orbitAngle = 290.0, radius = 95000.0, density = 1900.0;
            var orbit = new OrbitProperties { semiMajorAxis = 1.65e8, meanAnomalyAtEpochDegrees = orbitAngle, tidallyLocked = true };
            double period = OrbitalMechanics.CircularPeriod(parentMu, orbit.semiMajorAxis);
            var p = new PlanetPhysicalProperties
            {
                meanRadius = radius,
                mass = density * 4.0 / 3.0 * Math.PI * radius * radius * radius,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = period,
                rotationPhaseAtEpochDegrees = orbitAngle + 180.0,
                axialTiltDegrees = 0.0,
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
                Description = "Внешний малый спутник Золина неправильной формы, рыхлая груда обломков, покрытая красноватой толиновой пылью с планеты. Приливно захвачен. Параметры не заданы — оценка: R ≈ 95 км, ρ ≈ 1900 кг/м³, орбита ≈ 165 000 км.",
                Physical = p, Generation = g, Optics = new AtmosphereOpticalProperties(),
                Orbit = orbit,
                LaunchLatitude = 0.0, LaunchLongitude = 30.0,
                Hazards = new EnvironmentHazards { radiationLevel = 1 }
            };
        }
    }
}
