using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Procedural
{
    public enum Biome : byte
    {
        DeepOcean,
        ShallowOcean,
        SeaIce,
        Beach,
        Desert,
        Grassland,
        Forest,
        Tundra,
        Rock,
        Snow
    }

    public struct SurfaceSample
    {
        /// <summary>Solid ground height above the reference radius [m] (may be below sea level).</summary>
        public double TerrainHeight;
        /// <summary>Physical surface height: max(terrain, sea level) when an ocean exists [m].</summary>
        public double SurfaceHeight;
        public bool IsWater;
        public ColorRGB Albedo;
        public Biome Biome;
    }

    /// <summary>
    /// Deterministic surface function: unit direction (planet-fixed) → height, biome, colour.
    /// Same settings + same seed + same algorithm version ⇒ same surface, independent of query order or
    /// thread. The whole planet is never generated at once; chunks sample this function on demand.
    ///
    /// Algorithm version 1: domain-warped fBm continents, ridged multifractal mountains masked to land,
    /// fBm detail, seeded impact craters, latitude/elevation/moisture biomes, flatten zones.
    /// </summary>
    public sealed class PlanetSurfaceSampler : ISurfaceHeightProvider
    {
        public const int CurrentAlgorithmVersion = 1;

        public static bool IsAlgorithmVersionSupported(int version) => version == 1;

        /// <summary>Brightest albedo (sRGB luminance) a lake on a Rocky body may have and still be shaded as open liquid.</summary>
        const double MaxOpenLiquidLuminance = 0.42;

        // Salts for independent noise streams. Changing any of them changes every planet → new algorithm version.
        const ulong SaltWarpX = 0x1001, SaltWarpY = 0x1002, SaltWarpZ = 0x1003, SaltContinent = 0x2001,
            SaltMountain = 0x3001, SaltMountainMask = 0x3002, SaltDetail = 0x4001, SaltMoisture = 0x5001,
            SaltClimateJitter = 0x5002, SaltCraters = 0x6001;

        struct Crater
        {
            public Vector3d Direction;
            public double AngularRadius; // chord length on the unit sphere ≈ angle for small craters
            public double Depth;          // [m]
        }

        struct Flat
        {
            public Vector3d Direction;
            public double ChordRadius;
            public double Height;
        }

        struct Canyon
        {
            public Vector3d Start;      // unit vectors
            public Vector3d Normal;     // normal of the great circle through Start and End
            public Vector3d Tangent;    // Normal × Start: direction of travel at Start
            public double Arc;          // angle Start→End [rad]
            public double HalfWidth;    // [rad]
            public double Depth;        // [m]
        }

        readonly PlanetGenerationSettings s;
        readonly double planetRadius;
        readonly GradientNoise3D warpX, warpY, warpZ, continent, mountain, mountainMask, detail, moisture, climateJitter;
        readonly Crater[] craters;
        readonly Flat[] flats;
        readonly Canyon[] canyons;
        /// <summary>The artist colour map, or null (none set, or not registered → procedural colours).</summary>
        readonly SurfaceAlbedoMap map;

        public static PlanetSurfaceSampler Create(PlanetGenerationSettings settings, double planetRadius)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!IsAlgorithmVersionSupported(settings.algorithmVersion))
                throw new NotSupportedException(
                    "Surface algorithm version " + settings.algorithmVersion + " is not supported by this build " +
                    "(supported: " + CurrentAlgorithmVersion + "). Refusing to generate a different planet.");
            return new PlanetSurfaceSampler(settings, planetRadius);
        }

        PlanetSurfaceSampler(PlanetGenerationSettings settings, double radius)
        {
            s = settings.Clone(); // snapshot: later inspector edits cannot race with worker threads
            s.Sanitize();
            planetRadius = radius;
            ulong seed = (ulong)(uint)s.seed;

            warpX = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltWarpX));
            warpY = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltWarpY));
            warpZ = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltWarpZ));
            continent = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltContinent));
            mountain = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltMountain));
            mountainMask = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltMountainMask));
            detail = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltDetail));
            moisture = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltMoisture));
            climateJitter = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltClimateJitter));

            var rng = new DeterministicRandom(DeterministicRandom.Mix(seed, SaltCraters));
            craters = new Crater[s.craterCount + s.featureCraters.Length];
            double maxRim = 0.0;
            for (int i = 0; i < craters.Length; i++)
            {
                if (i >= s.craterCount)
                {
                    // Hand-placed landmark craters come after the random ones, so the random set is unaffected.
                    FeatureCrater f = s.featureCraters[i - s.craterCount];
                    craters[i] = new Crater
                    {
                        Direction = PlanetRotation.DirectionFromLatLon(f.latitudeDegrees * MathD.DegToRad, f.longitudeDegrees * MathD.DegToRad),
                        AngularRadius = Math.Max(1.0, f.radius) / planetRadius,
                        Depth = 2.0 * f.depthRatio * Math.Max(1.0, f.radius)
                    };
                    maxRim = Math.Max(maxRim, craters[i].Depth * (0.6 * s.craterRimWidth * s.craterRimWidth + 0.1));
                    continue;
                }
                double u = rng.NextDouble();
                double r = s.craterMinRadius + (s.craterMaxRadius - s.craterMinRadius) * u * u * u; // many small, few large
                craters[i] = new Crater
                {
                    Direction = rng.UnitVector(),
                    AngularRadius = r / planetRadius,
                    Depth = 2.0 * s.craterDepthRatio * r
                };
                // Rim peak of CraterShape is 0.6·rimWidth² (+ smoothing margin), in units of Depth.
                maxRim = Math.Max(maxRim, craters[i].Depth * (0.6 * s.craterRimWidth * s.craterRimWidth + 0.1));
            }

            double maxCraterDepth = 0.0;
            for (int i = 0; i < craters.Length; i++) maxCraterDepth = Math.Max(maxCraterDepth, craters[i].Depth * 0.5);

            if (s.UsesAlbedoMap) SurfaceAlbedoMaps.TryGet(s.albedoMap, out map);

            canyons = new Canyon[s.featureCanyons.Length];
            double maxCanyonDepth = 0.0;
            for (int i = 0; i < canyons.Length; i++)
            {
                FeatureCanyon f = s.featureCanyons[i];
                Vector3d a = PlanetRotation.DirectionFromLatLon(f.startLatitudeDegrees * MathD.DegToRad, f.startLongitudeDegrees * MathD.DegToRad);
                Vector3d b = PlanetRotation.DirectionFromLatLon(f.endLatitudeDegrees * MathD.DegToRad, f.endLongitudeDegrees * MathD.DegToRad);
                Vector3d n = Vector3d.Cross(a, b);
                if (n.Magnitude < 1e-9) n = Vector3d.Cross(a, Math.Abs(a.z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX); // degenerate: any circle
                n = n.Normalized;
                canyons[i] = new Canyon
                {
                    Start = a,
                    Normal = n,
                    Tangent = Vector3d.Cross(n, a),
                    Arc = Math.Acos(MathD.Clamp(Vector3d.Dot(a, b), -1.0, 1.0)),
                    HalfWidth = 0.5 * Math.Max(1.0, f.width) / planetRadius,
                    Depth = Math.Max(0.0, f.depth)
                };
                maxCanyonDepth = Math.Max(maxCanyonDepth, canyons[i].Depth);
            }
            double mapRelief = map != null ? Math.Abs(s.albedoMapRelief) * map.MaxAbsSmoothBrightness : 0.0;

            double contMax = (1.0 + Math.Abs(s.continentBias)) * Math.Max(s.continentAmplitude, 0.0);
            double contMin = (1.0 + Math.Abs(s.continentBias)) * Math.Max(s.oceanDepthAmplitude, 0.0);
            MaxTerrainHeight = contMax + Math.Abs(s.mountainAmplitude) + Math.Abs(s.detailAmplitude) + maxRim + mapRelief;
            MinTerrainHeight = -(contMin + Math.Abs(s.detailAmplitude) + maxCraterDepth + maxCanyonDepth + mapRelief);

            flats = new Flat[s.flattenZones.Length];
            for (int i = 0; i < flats.Length; i++)
            {
                var z = s.flattenZones[i];
                Vector3d dir = PlanetRotation.DirectionFromLatLon(z.latitudeDegrees * MathD.DegToRad, z.longitudeDegrees * MathD.DegToRad);
                double h = RawTerrainHeight(dir, out _, out _, out _);
                double floor = (s.hasOcean ? s.seaLevel : MinTerrainHeight) + Math.Max(0.0, z.minHeightAboveSeaLevel);
                flats[i] = new Flat { Direction = dir, ChordRadius = Math.Max(1.0, z.radius) / planetRadius, Height = Math.Max(h, floor) };
                MaxTerrainHeight = Math.Max(MaxTerrainHeight, flats[i].Height);
            }
        }

        public PlanetGenerationSettings Settings => s.Clone();
        public int Seed => s.seed;
        public int AlgorithmVersion => s.algorithmVersion;
        public double PlanetRadius => planetRadius;
        public double MinTerrainHeight { get; }
        public double MaxTerrainHeight { get; }
        /// <summary>True when the artist colour map named in the settings was found and is used.</summary>
        public bool HasAlbedoMap => map != null;

        public double MinSurfaceHeight => s.hasOcean ? Math.Max(MinTerrainHeight, s.seaLevel) : MinTerrainHeight;
        public double MaxSurfaceHeight => s.hasOcean ? Math.Max(MaxTerrainHeight, s.seaLevel) : MaxTerrainHeight;

        public double TerrainHeight(Vector3d unitDirection) => ApplyFlatten(unitDirection, RawTerrainHeight(unitDirection, out _, out _, out _));

        public double SurfaceHeight(Vector3d unitDirection)
        {
            double h = TerrainHeight(unitDirection);
            return s.hasOcean && h < s.seaLevel ? s.seaLevel : h;
        }

        public SurfaceSample Sample(Vector3d unitDirection)
        {
            double h = ApplyFlatten(unitDirection, RawTerrainHeight(unitDirection, out Vector3d warped, out double craterFloor, out double canyonFloor));
            bool water = s.hasOcean && h < s.seaLevel;
            var sample = new SurfaceSample
            {
                TerrainHeight = h,
                SurfaceHeight = water ? s.seaLevel : h,
                IsWater = water
            };
            if (s.surfaceStyle == SurfaceStyle.Rocky)
            {
                ShadeRocky(unitDirection, warped, h, craterFloor, canyonFloor, ref sample);
                if (water) ShadeLake(h, ref sample);
            }
            else if (s.surfaceStyle == SurfaceStyle.GasGiant) ShadeGasGiant(unitDirection, warped, ref sample);
            else Shade(unitDirection, warped, h, water, ref sample);
            return sample;
        }

        /// <summary>
        /// Airless/dry body: lowland "seas" vs highlands, brightness variation, stains, dark crater floors. With an artist
        /// colour map the map gives the colour and the generator only adds its fine brightness variation on top.
        /// </summary>
        void ShadeRocky(Vector3d p, Vector3d q, double h, double craterFloor, double canyonFloor, ref SurfaceSample sample)
        {
            double basin = Math.Max(1.0, 0.25 * s.oceanDepthAmplitude);
            double t = MathD.SmoothStep(-basin, 0.15 * Math.Max(1.0, s.continentAmplitude), h);
            double variation = moisture.Fbm(q * s.moistureFrequency, 4, 2.0, 0.5);
            ColorRGB c;
            if (map != null)
            {
                double k = s.albedoMapDetail;
                c = map.Sample(p) * (float)(1.0 + k * 0.35 * variation);
                c = c * (float)(1.0 - k * s.craterFloorDarkening * craterFloor);
            }
            else
            {
                c = ColorRGB.Lerp(s.lowland, s.highland, t);
                c = c * (float)(0.92 + 0.35 * variation);
                if (s.stainCoverage > 0.0)
                {
                    double n = 0.5 + 0.9 * climateJitter.Fbm(q * s.stainFrequency, 4, 2.0, 0.5);
                    double w = MathD.SmoothStep(1.0 - s.stainCoverage, 1.0 - s.stainCoverage + 0.12, n);
                    c = ColorRGB.Lerp(c, s.stain, w);
                }
                c = c * (float)(1.0 - s.craterFloorDarkening * craterFloor);
            }
            if (canyonFloor > 0.0) c = c * (float)(1.0 - s.canyonDarkening * canyonFloor);
            sample.Albedo = c;
            sample.Biome = t < 0.5 ? Biome.Desert : Biome.Rock;
        }

        /// <summary>
        /// A lake or sea on a Rocky body (e.g. liquid CO₂ over an artist map): a clear liquid shows its bed, tinted by
        /// <see cref="PlanetGenerationSettings.shallowOcean"/> (transmission), fading with depth to
        /// <see cref="PlanetGenerationSettings.deepOcean"/> — the same 0–2500 m depth ramp as terrestrial oceans.
        /// </summary>
        void ShadeLake(double h, ref SurfaceSample sample)
        {
            double depth = s.seaLevel - h;
            ColorRGB bed = new ColorRGB(sample.Albedo.r * s.shallowOcean.r, sample.Albedo.g * s.shallowOcean.g, sample.Albedo.b * s.shallowOcean.b);
            ColorRGB c = ColorRGB.Lerp(bed, s.deepOcean, MathD.SmoothStep(0.0, 2500.0, depth));
            // The surface shader shades water-masked pixels brighter than luminance 0.45 as ice (SpaceSimShadeSurface):
            // keep open liquid over a bright bed just below that, so it gets the liquid's glitter and sky reflection.
            double lum = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
            if (lum > MaxOpenLiquidLuminance) c = c * (float)(MaxOpenLiquidLuminance / lum);
            sample.Albedo = c;
            sample.Biome = depth > 200.0 ? Biome.DeepOcean : Biome.ShallowOcean;
        }

        /// <summary>
        /// Gas giant cloud tops: belts and zones parallel to the equator, their edges bent by turbulence that is stretched
        /// east–west (streaks along the bands), plus oval storms where <see cref="PlanetGenerationSettings.stainCoverage"/> &gt; 0.
        /// </summary>
        void ShadeGasGiant(Vector3d p, Vector3d q, ref SurfaceSample sample)
        {
            var streak = new Vector3d(q.x, q.y, q.z * s.bandStretch);
            if (map != null)
            {
                // The artist's map draws the belts; only a faint east–west streak texture is added.
                double k = s.albedoMapDetail;
                sample.Albedo = map.Sample(p) * (float)(1.0 + k * 0.15 * climateJitter.Fbm(streak * 6.0, 4, 2.0, 0.5));
                sample.Biome = Biome.Desert;
                return;
            }
            double turbulence = moisture.Fbm(streak * 2.0, 5, 2.0, 0.5);
            double phase = Math.PI * s.bandCount * p.z + s.bandTurbulence * turbulence;
            double zone = MathD.SmoothStep(-0.35, 0.35, Math.Sin(phase));
            ColorRGB c = ColorRGB.Lerp(s.lowland, s.highland, (float)zone);
            c = c * (float)(0.95 + 0.10 * climateJitter.Fbm(streak * 6.0, 4, 2.0, 0.5));
            if (s.stainCoverage > 0.0)
            {
                double n = 0.5 + 0.9 * climateJitter.Fbm(new Vector3d(q.x, q.y, q.z * 2.0) * s.stainFrequency, 3, 2.0, 0.5);
                double w = MathD.SmoothStep(1.0 - s.stainCoverage, 1.0 - s.stainCoverage + 0.08, n);
                c = ColorRGB.Lerp(c, s.stain, (float)w);
            }
            sample.Albedo = c;
            sample.Biome = Biome.Desert;
        }

        double RawTerrainHeight(Vector3d p, out Vector3d q, out double craterFloor, out double canyonFloor)
        {
            craterFloor = 0.0;
            canyonFloor = 0.0;
            Vector3d wp = p * s.warpFrequency;
            q = p + new Vector3d(warpX.Fbm(wp, 3, 2.0, 0.5), warpY.Fbm(wp, 3, 2.0, 0.5), warpZ.Fbm(wp, 3, 2.0, 0.5)) * s.warpStrength;

            double c = continent.Fbm(q * s.continentFrequency, s.continentOctaves, s.lacunarity, s.persistence) + s.continentBias;
            if (map != null && s.albedoMapCoastline > 0.0)
            {
                // The artist's map decides land and sea; small-scale noise (wavelengths ≈ 30–300 km on a 6000-km planet, below
                // and around the map's pixel) roughens the coastline so it is not a blurred staircase. Far from the coast
                // |land| = 1 outweighs the noise, so the map's continents stay where the artist drew them.
                double coastNoise = continent.Fbm(q * 140.0, 5, 2.0, 0.5);
                c = s.albedoMapCoastline * map.SmoothLand(p) + 0.45 * s.albedoMapCoastline * coastNoise
                    + 0.25 * (c - s.continentBias) * s.albedoMapCoastline;
            }
            double h = c >= 0.0 ? c * s.continentAmplitude : c * s.oceanDepthAmplitude;

            double land = MathD.SmoothStep(-0.02, 0.12, c);
            double mask = MathD.SmoothStep(0.02, 0.30, c) *
                          MathD.SmoothStep(-0.15, 0.35, mountainMask.Fbm(q * 1.3, 3, 2.0, 0.5));
            if (s.coastalMountainBoost > 0.0)
            {
                // Coastal ranges near the equator: land just above sea level, low latitude.
                double coast = MathD.SmoothStep(0.0, 0.06, c) * (1.0 - MathD.SmoothStep(0.10, 0.22, c));
                double tropics = 1.0 - MathD.SmoothStep(0.15, 0.45, Math.Abs(p.z));
                mask = Math.Min(1.0, mask + s.coastalMountainBoost * coast * tropics);
            }
            if (mask > 0.0)
            {
                double ridge = mountain.Ridged(q * s.mountainFrequency, s.mountainOctaves, s.lacunarity, 0.5);
                h += ridge * ridge * s.mountainAmplitude * mask;
            }

            if (s.detailOctaves > 0)
                h += detail.Fbm(q * s.detailFrequency, s.detailOctaves, s.lacunarity, 0.5) * s.detailAmplitude * (0.35 + 0.65 * land);

            for (int i = 0; i < craters.Length; i++)
            {
                ref readonly Crater cr = ref craters[i];
                double reach = cr.AngularRadius * (1.0 + s.craterRimWidth);
                // Cheap reject using the dot product before computing the chord length.
                double dot = Vector3d.Dot(p, cr.Direction);
                if (dot < 1.0 - 0.5 * reach * reach) continue;
                double x = (p - cr.Direction).Magnitude / cr.AngularRadius;
                h += CraterShape(x, s.craterRimWidth) * cr.Depth;
                if (x < 1.0) craterFloor = Math.Max(craterFloor, 1.0 - x * x);
            }

            if (map != null && s.albedoMapRelief != 0.0) h += map.SmoothBrightness(p) * s.albedoMapRelief;

            for (int i = 0; i < canyons.Length; i++)
            {
                ref readonly Canyon cn = ref canyons[i];
                double off = Vector3d.Dot(p, cn.Normal);              // sin of the angle to the great circle
                double across = Math.Abs(off) / cn.HalfWidth;
                if (across >= 1.0) continue;
                Vector3d inPlane = p - cn.Normal * off;
                double along = Math.Atan2(Vector3d.Dot(inPlane, cn.Tangent), Vector3d.Dot(inPlane, cn.Start));
                if (along <= 0.0 || along >= cn.Arc) continue;
                // Flat floor out to 55 % of the half-width, steep walls; ends taper over 10 % of the length.
                double taper = MathD.SmoothStep(0.0, 0.1 * cn.Arc, along) * MathD.SmoothStep(cn.Arc, 0.9 * cn.Arc, along);
                double floor = (1.0 - MathD.SmoothStep(0.55, 1.0, across)) * taper;
                h -= floor * cn.Depth;
                canyonFloor = Math.Max(canyonFloor, floor);
            }
            return h;
        }

        /// <summary>Bowl + raised rim, built from smooth min/max so it is continuous and exactly 0 outside the rim.</summary>
        static double CraterShape(double x, double rimWidth)
        {
            double cavity = x * x - 1.0;
            double rimX = Math.Min(x - 1.0 - rimWidth, 0.0);
            double rim = 0.6 * rimX * rimX;
            return MathD.SmoothMax(MathD.SmoothMin(cavity, rim, 0.25), -0.5, 0.25);
        }

        double ApplyFlatten(Vector3d p, double h)
        {
            for (int i = 0; i < flats.Length; i++)
            {
                ref readonly Flat f = ref flats[i];
                double d = (p - f.Direction).Magnitude;
                if (d >= 2.0 * f.ChordRadius) continue;
                double w = 1.0 - MathD.SmoothStep(f.ChordRadius, 2.0 * f.ChordRadius, d);
                h = MathD.Lerp(h, f.Height, w);
            }
            return h;
        }

        void Shade(Vector3d p, Vector3d q, double h, bool water, ref SurfaceSample sample)
        {
            // Climate scale 0 (polar) .. 1 (equatorial); p.z = sin(latitude) in the planet-fixed frame.
            double temp = 1.0 - p.z * p.z + 0.08 * climateJitter.Fbm(q * 3.0, 3, 2.0, 0.5)
                          - Math.Max(0.0, h - s.seaLevel) * s.elevationCooling;
            double wet = MathD.Clamp01(0.5 + 0.9 * moisture.Fbm(q * s.moistureFrequency, 4, 2.0, 0.5));
            if (s.equatorialAridity > 0.0)
                wet = MathD.Clamp01(wet - s.equatorialAridity * (1.0 - MathD.SmoothStep(0.0, 0.35, Math.Abs(p.z))));

            if (water)
            {
                double depth = s.seaLevel - h;
                sample.Albedo = ColorRGB.Lerp(s.shallowOcean, s.deepOcean, MathD.SmoothStep(0.0, 2500.0, depth));
                sample.Biome = depth > 200.0 ? Biome.DeepOcean : Biome.ShallowOcean;
                double ice = MathD.SmoothStep(0.07, 0.02, temp);
                if (ice > 0.0)
                {
                    sample.Albedo = ColorRGB.Lerp(sample.Albedo, s.seaIce, ice);
                    if (ice > 0.5) sample.Biome = Biome.SeaIce;
                }
                return;
            }

            double elevation = h - s.seaLevel;
            if (map != null)
            {
                ShadeMappedLand(p, q, elevation, temp, ref sample);
                return;
            }
            ColorRGB c = s.grassland;
            Biome b = Biome.Grassland;

            double forestW = MathD.SmoothStep(0.52, 0.72, wet) * MathD.SmoothStep(0.25, 0.4, temp);
            c = ColorRGB.Lerp(c, s.forest, forestW);
            if (forestW > 0.5) b = Biome.Forest;

            double desertW = MathD.SmoothStep(0.38, 0.22, wet) * MathD.SmoothStep(0.45, 0.65, temp);
            c = ColorRGB.Lerp(c, s.desert, desertW);
            if (desertW > 0.5) b = Biome.Desert;

            double tundraW = MathD.SmoothStep(0.33, 0.2, temp);
            c = ColorRGB.Lerp(c, s.tundra, tundraW);
            if (tundraW > 0.5) b = Biome.Tundra;

            double rockW = MathD.SmoothStep(1800.0, 3200.0, elevation);
            c = ColorRGB.Lerp(c, s.rock, rockW);
            if (rockW > 0.5) b = Biome.Rock;

            double snowW = Math.Max(MathD.SmoothStep(0.12, 0.05, temp), MathD.SmoothStep(3900.0, 4800.0, elevation));
            c = ColorRGB.Lerp(c, s.snow, snowW);
            if (snowW > 0.5) b = Biome.Snow;

            if (s.hasOcean)
            {
                double beachW = 1.0 - MathD.SmoothStep(5.0, 45.0, elevation);
                if (beachW > 0.0 && temp > 0.15)
                {
                    c = ColorRGB.Lerp(c, s.beach, beachW);
                    if (beachW > 0.5) b = Biome.Beach;
                }
            }

            sample.Albedo = c;
            sample.Biome = b;
        }

        /// <summary>
        /// Land of a terrestrial body with an artist map: the map gives the colour (green plains, ochre deserts, white ice), the
        /// generator adds fine brightness variation (so close up it is not one flat pixel colour), bare rock and snow on high
        /// mountains, and beaches. The biome is read from the colour.
        /// </summary>
        void ShadeMappedLand(Vector3d p, Vector3d q, double elevation, double temp, ref SurfaceSample sample)
        {
            double k = s.albedoMapDetail;
            double coarse = moisture.Fbm(q * s.moistureFrequency, 4, 2.0, 0.5);
            double fine = climateJitter.Fbm(q * 900.0, 4, 2.0, 0.5);
            ColorRGB c = map.Sample(p) * (float)(1.0 + k * (0.3 * coarse + 0.35 * fine));
            double lum = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
            Biome b = lum > 0.5 ? Biome.Snow : c.r > c.g * 1.02 ? Biome.Desert : lum < 0.3 ? Biome.Forest : Biome.Grassland;

            double rockW = MathD.SmoothStep(1800.0, 3200.0, elevation);
            c = ColorRGB.Lerp(c, s.rock, rockW);
            if (rockW > 0.5) b = Biome.Rock;
            double snowW = MathD.SmoothStep(3900.0, 4800.0, elevation);
            c = ColorRGB.Lerp(c, s.snow, snowW);
            if (snowW > 0.5) b = Biome.Snow;
            if (s.hasOcean && b != Biome.Snow)
            {
                double beachW = 1.0 - MathD.SmoothStep(5.0, 45.0, elevation);
                if (beachW > 0.0 && temp > 0.15)
                {
                    c = ColorRGB.Lerp(c, s.beach, beachW);
                    if (beachW > 0.5) b = Biome.Beach;
                }
            }
            sample.Albedo = c;
            sample.Biome = b;
        }
    }
}
