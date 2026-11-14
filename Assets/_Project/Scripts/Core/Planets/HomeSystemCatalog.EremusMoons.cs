using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Procedural;
using SpaceSim.Units;

namespace SpaceSim.Planets
{
    /// <summary>
    /// The eight major moons of Eremus (designer data: M, R, ρ, T, rotation and orbital periods, g, a, pericentre and
    /// apocentre distances, inclination; flattening for Nevermour and Gandalf).
    ///
    /// Inputs used: M, R, a, e = (r_apo − r_peri)/(r_apo + r_peri), i, the rotation period and T. ρ and g are derived and
    /// agree with the quoted values within 0.3 %. The orbital period is not an input: it follows from Kepler's third law with
    /// Eremus' μ, and agrees with the quoted period within 0.4 % for every moon (Ori: 0.472 d vs 0.474 d is the largest).
    /// Interpretation (not given):
    /// • The inclinations (4.13…4.18°) are measured from the ecliptic (the simulation's reference plane), all with the same
    ///   ascending node (Ω = 0, ω = 0). Eremus' obliquity, which was not given, is set to 4.15° about the same axis, so the
    ///   seven regular moons orbit almost exactly in its equatorial plane, as the regular moons of a giant do; Gandalf (8.14°,
    ///   the most eccentric, not tidally locked) is the odd one out, like an outer moon.
    /// • Each moon's spin axis is perpendicular to its own orbit (axial tilt = inclination about the same axis).
    /// • Tidally locked moons (rotation = revolution) keep the prime meridian (longitude 0) towards Eremus; on an eccentric
    ///   orbit the planet then librates by up to ±2e rad in the sky, as the Moon's does.
    /// • Gandalf's mass is quoted as 4.59·10⁻⁴ kg — a typo: g·R²/G = 2.754·10²¹ kg and ρ·V = 2.760·10²¹ kg; 2.75·10²¹ kg is used.
    /// • Nevermour's pericentre and apocentre are quoted swapped (0.00329 / 0.00328 AU); they are taken as 0.00328 / 0.00329.
    /// • Polar flattening (Nevermour 0.131 %, Gandalf 0.685 %) is not modelled: shapes and gravity are spherical, as for every
    ///   body (no J2). Gandalf's reference photo is strongly egg-shaped; the data's 0.685 % is followed.
    /// • Radiation: moons inside ≈ 15 Eremus radii (Ori … Balin) orbit in its radiation belts (level 2, like Io and Europa);
    ///   Coloss and Gandalf (22 and 26 radii) get level 1 (like Callisto).
    /// • Only Coloss has an atmosphere (the reference photo shows clouds and seas): a Titan analogue, see <see cref="Coloss"/>.
    ///   The others are airless (too small and/or too close to the giant to keep one).
    /// • Colours and relief are procedural, tuned by eye to the reference photos in Documentation/References/EremusMoons (whole-sphere
    ///   renders, not maps). Pads are on the near side, at ≈ 10:00 local time when the game starts.
    /// Spheres of influence: the four smallest moons have a Laplace sphere of only 1.2–2.8 radii, so the 3-radius minimum of
    /// <see cref="PatchedConicSystem"/> applies; no two moons' spheres can overlap at any point of their orbits, and all lie deep
    /// inside Eremus' sphere (Gandalf's apocentre is at 12 % of it).
    /// </summary>
    public static partial class HomeSystemCatalog
    {
        public const string OriId = "ori";
        public const string OinId = "oin";
        public const string BifurId = "bifur";
        public const string NevermourId = "nevermour";
        public const string DwalinId = "dwalin";
        public const string BalinId = "balin";
        /// <summary>The largest moon of Eremus: N₂ air and methane–ethane seas (a Titan analogue).</summary>
        public const string ColossId = "coloss";
        /// <summary>Outermost major moon of Eremus, the only one that is not tidally locked.</summary>
        public const string GandalfId = "gandalf";

        /// <summary>The eight moons of Eremus, from the inside out.</summary>
        public static readonly string[] EremusMoonIds = { OriId, OinId, BifurId, NevermourId, DwalinId, BalinId, ColossId, GandalfId };

        /// <summary>Position of Eremus on its orbit at t = 0 [deg] (its moons see the star in the same direction).</summary>
        const double EremusOrbitAngle = 300.0;
        /// <summary>Eremus' obliquity [deg] (not given; chosen so the regular moons orbit in its equatorial plane).</summary>
        const double EremusObliquity = 4.15;
        /// <summary>Gandalf's mass [kg]: from g·R²/G and ρ·V (the quoted 4.59·10⁻⁴ kg is a typo).</summary>
        public const double GandalfAssumedMass = 2.75e21;

        sealed class MoonData
        {
            public string Id, Name, Description;
            public double Mass, Radius, TemperatureC;
            /// <summary>Semi-major axis, pericentre and apocentre distances [m].</summary>
            public double SemiMajorAxis, Pericentre, Apocentre;
            public double InclinationDegrees;
            /// <summary>Mean anomaly at t = 0 [deg].</summary>
            public double OrbitAngle;
            /// <summary>Sidereal rotation period [s]; 0 = tidally locked (rotation = Kepler period).</summary>
            public double RotationPeriod;
            public double PadLatitude;
            /// <summary>Only for a freely rotating moon (a locked one gets its pad at 10:00 automatically).</summary>
            public double PadLongitude;
            public int Radiation;
            public double SpaceBoundary = 10000.0;
            /// <summary>null = airless.</summary>
            public AtmosphereProperties Atmosphere;
            public AtmosphereOpticalProperties Optics;
            public PlanetGenerationSettings Generation;
        }

        static void AddEremusMoons(List<CelestialBodyPreset> list, double eremusMu)
        {
            list.Add(EremusMoon(Ori(), eremusMu));
            list.Add(EremusMoon(Oin(), eremusMu));
            list.Add(EremusMoon(Bifur(), eremusMu));
            list.Add(EremusMoon(Nevermour(), eremusMu));
            list.Add(EremusMoon(Dwalin(), eremusMu));
            list.Add(EremusMoon(Balin(), eremusMu));
            list.Add(EremusMoon(Coloss(), eremusMu));
            list.Add(EremusMoon(Gandalf(), eremusMu));
        }

        static double Wrap180(double degrees)
        {
            double d = (degrees + 180.0) % 360.0;
            if (d < 0.0) d += 360.0;
            return d - 180.0;
        }

        static CelestialBodyPreset EremusMoon(MoonData m, double eremusMu)
        {
            bool locked = m.RotationPeriod <= 0.0;
            var orbit = new OrbitProperties
            {
                semiMajorAxis = m.SemiMajorAxis,
                eccentricity = (m.Apocentre - m.Pericentre) / (m.Apocentre + m.Pericentre),
                inclinationDegrees = m.InclinationDegrees,
                meanAnomalyAtEpochDegrees = m.OrbitAngle,
                tidallyLocked = locked
                // period = 0: Kepler's third law with Eremus' μ (the quoted periods agree within 0.4 %).
            };
            double padLon;
            double phase;
            if (locked)
            {
                // Prime meridian faces Eremus (direction angle + 180°); the star is where it is seen from Eremus (angle + 180°),
                // so the sub-solar longitude is EremusOrbitAngle − angle; the pad is 30° west of it (≈ 10:00).
                phase = m.OrbitAngle + 180.0;
                padLon = Wrap180(EremusOrbitAngle - m.OrbitAngle - 30.0);
            }
            else
            {
                padLon = m.PadLongitude;
                phase = PhaseForLocalTime(EremusOrbitAngle, padLon, 10.0);
            }
            AtmosphereProperties air = m.Atmosphere ?? new AtmosphereProperties
            {
                hasAtmosphere = false, surfaceDensity = 0.0, surfacePressure = 0.0,
                surfaceTemperature = m.TemperatureC + 273.15,
                composition = "нет атмосферы"
            };
            var p = new PlanetPhysicalProperties
            {
                meanRadius = m.Radius,
                mass = m.Mass,
                gravitySource = GravitationalParameterSource.FromMass,
                siderealRotationPeriod = locked ? OrbitalMechanics.CircularPeriod(eremusMu, m.SemiMajorAxis) : m.RotationPeriod,
                axialTiltDegrees = m.InclinationDegrees, // spin axis perpendicular to the orbit
                rotationPhaseAtEpochDegrees = phase,
                spaceBoundaryAltitude = m.SpaceBoundary,
                atmosphere = air
            };
            p.measuredGravitationalParameter = p.GravitationalParameter;
            if (air.hasAtmosphere)
            {
                air.scaleHeight = AtmosphericModel.IsothermalScaleHeight(air.surfaceTemperature, air.meanMolarMass, p.SurfaceGravity);
                air.surfaceDensity = AtmosphericModel.IdealGasDensity(air.surfacePressure, air.meanMolarMass, air.surfaceTemperature);
            }
            return new CelestialBodyPreset
            {
                Id = m.Id, DisplayName = m.Name, ParentId = EremusId, Description = m.Description,
                Physical = p, Generation = m.Generation, Optics = m.Optics ?? new AtmosphereOpticalProperties(),
                Orbit = orbit,
                LaunchLatitude = m.PadLatitude, LaunchLongitude = padLon,
                Hazards = new EnvironmentHazards { radiationLevel = m.Radiation }
            };
        }

        /// <summary>Ori — innermost: pale peach, rough knobbly cratered highlands and smooth light plains.</summary>
        static MoonData Ori() => new MoonData
        {
            Id = OriId, Name = "Ори (Ori)",
            Description = "Ближайший к Еремусу крупный спутник, внутри его радиационных поясов. Бледно-персиковый: бугристые нагорья, изрытые мелкими кратерами, и гладкие светлые равнины. Приливно захвачен, без атмосферы. −135 °C, g = 0.258 м/с², сутки = обращение = 0.474 земных.",
            Mass = 1.37e20, Radius = 188335.0, TemperatureC = -135.0,
            SemiMajorAxis = 2.44e8, Pericentre = 2.41e8, Apocentre = 2.47e8, InclinationDegrees = 4.17,
            OrbitAngle = 250.0, PadLatitude = 6.0, Radiation = 2, SpaceBoundary = 5000.0,
            Generation = new PlanetGenerationSettings
            {
                seed = 71203, surfaceStyle = SurfaceStyle.Rocky, hasOcean = false,
                continentFrequency = 1.3, continentAmplitude = 1200, oceanDepthAmplitude = 900, warpStrength = 0.25,
                mountainFrequency = 4.0, mountainAmplitude = 900,
                detailFrequency = 140, detailAmplitude = 90,
                craterCount = 380, craterMinRadius = 300, craterMaxRadius = 12000, craterDepthRatio = 0.12,
                lowland = new ColorRGB(0.74f, 0.59f, 0.45f),   // smooth light plains
                highland = new ColorRGB(0.63f, 0.49f, 0.37f),  // rough knobbly highlands
                craterFloorDarkening = 0.12, moistureFrequency = 3.0,
                cloudCoverage = 0.0
            }
        };

        /// <summary>Oin — rust-red, fractured ridges, a field of dark pits.</summary>
        static MoonData Oin() => new MoonData
        {
            Id = OinId, Name = "Оин (Oin)",
            Description = "Второй спутник Еремуса. Ржаво-красный, поверхность изрезана грядами и разломами, на одном краю — поле тёмных ям. Приливно захвачен, без атмосферы, в радиационных поясах. −139 °C, g = 0.439 м/с², сутки = обращение = 0.711 земных.",
            Mass = 7.81e20, Radius = 344640.0, TemperatureC = -139.0,
            SemiMajorAxis = 0.00214 * AU, Pericentre = 0.00210 * AU, Apocentre = 0.00219 * AU, InclinationDegrees = 4.18,
            OrbitAngle = 235.0, PadLatitude = -8.0, Radiation = 2, SpaceBoundary = 8000.0,
            Generation = new PlanetGenerationSettings
            {
                seed = 82731, surfaceStyle = SurfaceStyle.Rocky, hasOcean = false,
                continentFrequency = 1.5, continentAmplitude = 1500, oceanDepthAmplitude = 1500, warpFrequency = 2.2, warpStrength = 0.35,
                mountainFrequency = 5.0, mountainAmplitude = 1300,
                detailFrequency = 150, detailAmplitude = 100,
                craterCount = 300, craterMinRadius = 400, craterMaxRadius = 15000, craterDepthRatio = 0.1,
                lowland = new ColorRGB(0.36f, 0.17f, 0.10f),
                highland = new ColorRGB(0.52f, 0.27f, 0.16f),
                stain = new ColorRGB(0.15f, 0.08f, 0.06f), stainCoverage = 0.08, stainFrequency = 9.0, // dark pits
                craterFloorDarkening = 0.3, moistureFrequency = 3.0,
                cloudCoverage = 0.0
            }
        };

        /// <summary>Bifur — pale cream, pitted all over by small craters, a smoother equatorial belt.</summary>
        static MoonData Bifur() => new MoonData
        {
            Id = BifurId, Name = "Бифур (Bifur)",
            Description = "Третий спутник Еремуса. Светло-кремовый, весь изрыт мелкими кратерами; у экватора — более гладкий пояс. Приливно захвачен, без атмосферы, в радиационных поясах. −148 °C, g = 0.332 м/с², сутки = обращение = 0.95 земных.",
            Mass = 3.68e20, Radius = 271926.0, TemperatureC = -148.0,
            SemiMajorAxis = 0.0026 * AU, Pericentre = 0.00254 * AU, Apocentre = 0.00266 * AU, InclinationDegrees = 4.17,
            OrbitAngle = 270.0, PadLatitude = 4.0, Radiation = 2, SpaceBoundary = 6000.0,
            Generation = new PlanetGenerationSettings
            {
                seed = 93157, surfaceStyle = SurfaceStyle.Rocky, hasOcean = false,
                continentFrequency = 1.1, continentAmplitude = 900, oceanDepthAmplitude = 900, warpStrength = 0.2,
                mountainFrequency = 3.5, mountainAmplitude = 700,
                detailFrequency = 160, detailAmplitude = 80,
                craterCount = 560, craterMinRadius = 300, craterMaxRadius = 8000, craterDepthRatio = 0.14,
                lowland = new ColorRGB(0.77f, 0.63f, 0.48f),
                highland = new ColorRGB(0.70f, 0.56f, 0.42f),
                craterFloorDarkening = 0.1, moistureFrequency = 2.5,
                cloudCoverage = 0.0
            }
        };

        /// <summary>Nevermour — orange, a large darker smooth "sea", mottled rough terrain.</summary>
        static MoonData Nevermour() => new MoonData
        {
            Id = NevermourId, Name = "Невермор (Nevermour)",
            Description = "Четвёртый спутник Еремуса. Оранжевый, с большим тёмным гладким «морем» и пятнистыми неровными нагорьями. Приливно захвачен, без атмосферы, в радиационных поясах. Полярное сжатие 0.131 % (не моделируется). −155 °C, g = 0.582 м/с², сутки = обращение = 1.35 земных.",
            Mass = 1.86e21, Radius = 461554.0, TemperatureC = -155.0,
            // Quoted as pericentre 0.00329, apocentre 0.00328 AU: swapped.
            SemiMajorAxis = 0.00329 * AU, Pericentre = 0.00328 * AU, Apocentre = 0.00329 * AU, InclinationDegrees = 4.16,
            OrbitAngle = 260.0, PadLatitude = 10.0, Radiation = 2,
            Generation = new PlanetGenerationSettings
            {
                seed = 30419, surfaceStyle = SurfaceStyle.Rocky, hasOcean = false,
                continentFrequency = 0.9, continentBias = -0.05, continentAmplitude = 1500, oceanDepthAmplitude = 2200, warpStrength = 0.3,
                mountainFrequency = 3.5, mountainAmplitude = 1100,
                detailFrequency = 130, detailAmplitude = 90,
                craterCount = 250, craterMinRadius = 400, craterMaxRadius = 20000, craterDepthRatio = 0.1,
                lowland = new ColorRGB(0.54f, 0.29f, 0.15f),   // the dark smooth "sea"
                highland = new ColorRGB(0.74f, 0.50f, 0.30f),
                stain = new ColorRGB(0.47f, 0.25f, 0.13f), stainCoverage = 0.15, stainFrequency = 6.0,
                craterFloorDarkening = 0.15, moistureFrequency = 3.0,
                cloudCoverage = 0.0
            }
        };

        /// <summary>Dwalin — smooth pale peach, few craters, faint fractures.</summary>
        static MoonData Dwalin() => new MoonData
        {
            Id = DwalinId, Name = "Двалин (Dwalin)",
            Description = "Пятый спутник Еремуса. Гладкий, бледно-персиковый, почти без кратеров, с едва заметными трещинами. Приливно захвачен, без атмосферы, в радиационных поясах. −152 °C, g = 1.36 м/с², сутки = обращение = 2.40 земных.",
            Mass = 2.99e22, Radius = 1210924.0, TemperatureC = -152.0,
            SemiMajorAxis = 0.00482 * AU, Pericentre = 0.00475 * AU, Apocentre = 0.00489 * AU, InclinationDegrees = 4.16,
            OrbitAngle = 240.0, PadLatitude = 12.0, Radiation = 2, SpaceBoundary = 15000.0,
            Generation = new PlanetGenerationSettings
            {
                seed = 44862, surfaceStyle = SurfaceStyle.Rocky, hasOcean = false,
                continentFrequency = 1.2, continentAmplitude = 600, oceanDepthAmplitude = 600, warpFrequency = 2.5, warpStrength = 0.3,
                mountainFrequency = 6.0, mountainAmplitude = 350,
                detailFrequency = 120, detailAmplitude = 40,
                craterCount = 60, craterMinRadius = 2000, craterMaxRadius = 25000, craterDepthRatio = 0.05,
                lowland = new ColorRGB(0.74f, 0.58f, 0.42f),
                highland = new ColorRGB(0.81f, 0.66f, 0.49f),
                craterFloorDarkening = 0.08, moistureFrequency = 2.0,
                cloudCoverage = 0.0
            }
        };

        /// <summary>
        /// Balin — peach plains with bright spots and a black field near the south: the warmest moon (−102 °C) is taken as
        /// volcanically active (assumption: tidal heating), the black field as fresh lava; the bright spots as frost deposits.
        /// </summary>
        static MoonData Balin() => new MoonData
        {
            Id = BalinId, Name = "Балин (Balin)",
            Description = "Шестой спутник Еремуса и самый тёплый из них (−102 °C). Персиковые равнины в светлых пятнах инея; на юге — чёрное поле застывшей лавы (допущение: приливный разогрев недр). Приливно захвачен, без атмосферы, на краю радиационных поясов. g = 1.55 м/с², сутки = обращение = 3.73 земных.",
            Mass = 3.72e22, Radius = 1266677.0, TemperatureC = -102.0,
            SemiMajorAxis = 0.00646 * AU, Pericentre = 0.00641 * AU, Apocentre = 0.00651 * AU, InclinationDegrees = 4.15,
            OrbitAngle = 275.0, PadLatitude = 15.0, Radiation = 2, SpaceBoundary = 15000.0,
            Generation = new PlanetGenerationSettings
            {
                seed = 57390, surfaceStyle = SurfaceStyle.Rocky, hasOcean = false,
                continentFrequency = 1.0, continentAmplitude = 900, oceanDepthAmplitude = 1200, warpStrength = 0.2,
                mountainFrequency = 3.5, mountainAmplitude = 700,
                detailFrequency = 130, detailAmplitude = 60,
                // Few craters, their floors flooded with dark lava.
                craterCount = 40, craterMinRadius = 3000, craterMaxRadius = 40000, craterDepthRatio = 0.05,
                lowland = new ColorRGB(0.70f, 0.52f, 0.38f),
                highland = new ColorRGB(0.80f, 0.62f, 0.46f),
                stain = new ColorRGB(0.93f, 0.85f, 0.72f), stainCoverage = 0.10, stainFrequency = 14.0, // bright frost spots
                craterFloorDarkening = 0.75, moistureFrequency = 2.5,
                cloudCoverage = 0.0,
                // The black lava field near the southern limb.
                featureCraters = new[]
                {
                    new FeatureCrater { latitudeDegrees = -52.0, longitudeDegrees = 35.0, radius = 140000, depthRatio = 0.012 },
                    new FeatureCrater { latitudeDegrees = -58.0, longitudeDegrees = 62.0, radius = 90000, depthRatio = 0.012 },
                    new FeatureCrater { latitudeDegrees = -47.0, longitudeDegrees = 80.0, radius = 60000, depthRatio = 0.015 }
                }
            }
        };

        /// <summary>
        /// Coloss — the largest moon (0.24 M⊕, g = 5.16 m/s²), a Titan analogue. At −179 °C (94 K, Titan's own surface
        /// temperature) the teal "ocean" of the reference photo cannot be water; it is taken as seas of liquid methane–ethane
        /// under a nitrogen atmosphere, the tan land as ice bedrock under organic sediment, the clouds as methane clouds.
        /// Interpretation (not given): N₂ ≈ 95 %, CH₄ ≈ 5 %, 1.5 bar (Titan: 1.47 bar). Liquid methane needs more than its vapour
        /// pressure (≈ 0.17 bar at 94 K; triple point 90.7 K, 0.117 bar) and stays below boiling (≈ 118 K at 1.5 bar).
        /// The seas are blue-green because methane absorbs red light (bands at 619, 727, 890 nm): light scattered back from deep
        /// in the clear liquid is teal, for the same reason Uranus and Neptune look cyan. Jeans parameter for N₂ at a ≈ 150 K
        /// exobase λ = (v_esc / v_th)² ≈ 490 (≫ 20): the air is stable. No Titan-like orange haze (the photo shows a clear disk).
        /// </summary>
        static MoonData Coloss()
        {
            var a = new AtmosphereProperties
            {
                hasAtmosphere = true,
                surfacePressure = 1.5e5,
                composition = "N₂ ≈ 95 %, CH₄ ≈ 5 % (давление и состав не заданы — допущение: как у Титана, 1.5 бар)",
                surfaceTemperature = 94.15,       // −179 °C
                meanMolarMass = 0.95 * 0.028013 + 0.05 * 0.016043,
                coRotationFactor = 1.0
            };
            // Rayleigh scattering scales with the number density; N₂ scatters ≈ 1.05 × as much per molecule as air.
            double numberRatio = (a.surfacePressure / a.surfaceTemperature) / (101325.0 / 288.15);
            var optics = new AtmosphereOpticalProperties
            {
                rayleighScattering = new Vector3d(5.802e-6, 13.558e-6, 33.1e-6) * (1.05 * numberRatio),
                mieScattering = 3.0e-6, mieExtinctionRatio = 1.1, mieScaleHeight = 3000.0, mieAnisotropy = 0.75
            };
            return new MoonData
            {
                Id = ColossId, Name = "Колосс (Coloss)",
                Description = "Седьмой и самый большой спутник Еремуса (0.24 M⊕, g = 5.16 м/с²) — аналог Титана. При −179 °C «океан» не может быть водой: это моря жидкого метана и этана под азотной атмосферой (давление не задано — допущение 1.5 бар); бирюзовый цвет — метан поглощает красный свет. Суша — лёд и порода под рыжеватыми органическими осадками, облака — метановые. Приливно захвачен, сутки = обращение = 9.42 земных.",
                Mass = 1.42e24, Radius = 4282711.0, TemperatureC = -179.0,
                SemiMajorAxis = 0.012 * AU, Pericentre = 0.0117 * AU, Apocentre = 0.0123 * AU, InclinationDegrees = 4.13,
                OrbitAngle = 290.0, PadLatitude = 18.0, Radiation = 1, SpaceBoundary = 100000.0, // ≈ 18 scale heights
                Atmosphere = a, Optics = optics,
                Generation = new PlanetGenerationSettings
                {
                    seed = 66127, surfaceStyle = SurfaceStyle.Rocky, hasOcean = true, seaLevel = 0.0,
                    continentFrequency = 1.6, continentBias = -0.12, continentAmplitude = 1400, oceanDepthAmplitude = 2500, warpStrength = 0.3,
                    mountainFrequency = 3.0, mountainAmplitude = 800,
                    detailFrequency = 120, detailAmplitude = 50,
                    craterCount = 20, craterMinRadius = 3000, craterMaxRadius = 40000, craterDepthRatio = 0.04, // eroded by rain
                    lowland = new ColorRGB(0.66f, 0.50f, 0.36f),   // tan organic sediment
                    highland = new ColorRGB(0.82f, 0.68f, 0.52f),
                    craterFloorDarkening = 0.1, moistureFrequency = 2.5,
                    // Clear methane–ethane: red absorbed, the deep sea scatters back teal; the bed shows through the shallows.
                    shallowOcean = new ColorRGB(0.45f, 0.95f, 0.88f),
                    deepOcean = new ColorRGB(0.05f, 0.36f, 0.34f),
                    cloudCoverage = 0.3, cloudTint = new ColorRGB(1f, 1f, 1f), // methane clouds
                    // The teal seas of the artist map (HomeSystemCatalog.ArtistMaps → "coloss") decide where the liquid is.
                    albedoMapCoastline = 0.12
                }
            };
        }

        /// <summary>Gandalf — outermost, yellow-green with salmon tints, many bright round spots; rotates freely.</summary>
        static MoonData Gandalf() => new MoonData
        {
            Id = GandalfId, Name = "Гандальф (Gandalf)",
            Description = "Восьмой, самый далёкий крупный спутник Еремуса. Жёлто-зелёный с лососевыми оттенками, усыпан светлыми круглыми пятнами. Единственный не захваченный приливом: сутки 0.481 земных, обращение 12.4 суток; самая наклонная (8.14°) и вытянутая орбита. Полярное сжатие 0.685 % (не моделируется). −189 °C — холоднее предела скафандра, g = 0.798 м/с². Масса в данных — опечатка; принята 2.75·10²¹ кг (из g и ρ).",
            Mass = GandalfAssumedMass, Radius = 479950.0, TemperatureC = -189.0,
            SemiMajorAxis = 0.0144 * AU, Pericentre = 0.0137 * AU, Apocentre = 0.0152 * AU, InclinationDegrees = 8.14,
            OrbitAngle = 170.0, RotationPeriod = 0.481 * Day, PadLatitude = -5.0, PadLongitude = 20.0, Radiation = 1,
            Generation = new PlanetGenerationSettings
            {
                seed = 18845, surfaceStyle = SurfaceStyle.Rocky, hasOcean = false,
                continentFrequency = 1.2, continentAmplitude = 1000, oceanDepthAmplitude = 1000, warpFrequency = 2.0, warpStrength = 0.35,
                mountainFrequency = 3.0, mountainAmplitude = 800,
                detailFrequency = 140, detailAmplitude = 70,
                craterCount = 200, craterMinRadius = 500, craterMaxRadius = 15000, craterDepthRatio = 0.1,
                lowland = new ColorRGB(0.60f, 0.64f, 0.34f),   // yellow-green
                highland = new ColorRGB(0.74f, 0.62f, 0.46f),  // salmon
                stain = new ColorRGB(0.93f, 0.91f, 0.74f), stainCoverage = 0.12, stainFrequency = 16.0, // bright round spots
                craterFloorDarkening = 0.1, moistureFrequency = 3.0,
                cloudCoverage = 0.0
            }
        };
    }
}
