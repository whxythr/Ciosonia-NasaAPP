using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Procedural
{
    /// <summary>A region flattened to a constant height (launch site, future bases).</summary>
    [Serializable]
    public struct FlattenZone
    {
        public double latitudeDegrees;
        public double longitudeDegrees;
        /// <summary>Fully flat inside this ground distance [m]; blends back to terrain over the same distance.</summary>
        public double radius;
        /// <summary>The flat area is kept at least this high above sea level [m].</summary>
        public double minHeightAboveSeaLevel;
    }

    /// <summary>Colouring scheme of the surface.</summary>
    public enum SurfaceStyle
    {
        /// <summary>Earth-like: oceans, climate and moisture biomes.</summary>
        Terrestrial,
        /// <summary>Airless/dry rocky body: lowland/highland albedo, stains, darkened crater floors.</summary>
        Rocky,
        /// <summary>
        /// Gas giant: no solid surface. The reference sphere is the 1-bar level (the cloud tops); colours are zonal bands
        /// (belts = <see cref="PlanetGenerationSettings.lowland"/>, zones = <see cref="PlanetGenerationSettings.highland"/>,
        /// storms = <see cref="PlanetGenerationSettings.stain"/>) sheared by turbulence. Relief comes only from the amplitudes,
        /// which a gas giant sets to 0.
        /// </summary>
        GasGiant
    }

    /// <summary>A hand-placed crater (e.g. a recognisable landmark).</summary>
    [Serializable]
    public struct FeatureCrater
    {
        public double latitudeDegrees;
        public double longitudeDegrees;
        /// <summary>Rim radius [m].</summary>
        public double radius;
        /// <summary>Floor depth as a fraction of the radius.</summary>
        public double depthRatio;
    }

    /// <summary>
    /// A canyon along a great-circle arc from (lat1, lon1) to (lat2, lon2): flat floor, steep walls, tapered ends.
    /// </summary>
    [Serializable]
    public struct FeatureCanyon
    {
        public double startLatitudeDegrees;
        public double startLongitudeDegrees;
        public double endLatitudeDegrees;
        public double endLongitudeDegrees;
        /// <summary>Full width at the rim [m].</summary>
        public double width;
        /// <summary>Depth of the floor below the surroundings [m].</summary>
        public double depth;
    }

    /// <summary>
    /// Parameters of the deterministic surface generator. Together with <see cref="seed"/> and
    /// <see cref="algorithmVersion"/> they fully determine the surface. They describe appearance and relief
    /// only — mass, radius and gravity live in PlanetPhysicalProperties and are never touched by the seed.
    /// Frequencies are in cycles per planet radius (noise is sampled on the unit sphere).
    /// </summary>
    [Serializable]
    public class PlanetGenerationSettings
    {
        public int seed = 20251;
        /// <summary>Version of the surface algorithm these settings were created with. Never silently upgraded.</summary>
        public int algorithmVersion = PlanetSurfaceSampler.CurrentAlgorithmVersion;

        // ---- Continents ----
        public double continentFrequency = 1.1;
        public int continentOctaves = 6;
        public double lacunarity = 2.0;
        public double persistence = 0.5;
        /// <summary>Shifts the land/ocean balance; negative → more ocean.</summary>
        public double continentBias = -0.04;
        /// <summary>Land height per unit of continental noise [m].</summary>
        public double continentAmplitude = 5000.0;
        /// <summary>Ocean depth per unit of continental noise [m].</summary>
        public double oceanDepthAmplitude = 9000.0;
        public double warpFrequency = 1.6;
        public double warpStrength = 0.12;

        // ---- Mountains ----
        public double mountainFrequency = 3.2;
        public int mountainOctaves = 7;
        public double mountainAmplitude = 5200.0;

        // ---- Detail ----
        public double detailFrequency = 180.0;
        public int detailOctaves = 9;
        public double detailAmplitude = 160.0;

        // ---- Ocean ----
        public bool hasOcean = true;
        /// <summary>Sea level relative to the reference radius [m].</summary>
        public double seaLevel = 0.0;
        // On a Rocky body with an ocean (lakes over an artist map) the liquid is seen over its bed: shallow water shows the
        // bed colour × shallowOcean (transmission of a clear liquid), deep water fades to deepOcean.

        // ---- Craters ----
        public int craterCount = 24;
        public double craterMinRadius = 3000.0;
        public double craterMaxRadius = 45000.0;
        /// <summary>Floor depth as a fraction of crater radius.</summary>
        public double craterDepthRatio = 0.06;
        public double craterRimWidth = 0.45;

        // ---- Climate / biomes ----
        /// <summary>Temperature drop per metre of elevation, in units of the 0..1 climate scale.</summary>
        public double elevationCooling = 0.00008;
        public double moistureFrequency = 2.4;
        public ColorRGB deepOcean = new ColorRGB(0.015f, 0.05f, 0.16f);
        public ColorRGB shallowOcean = new ColorRGB(0.04f, 0.17f, 0.30f);
        public ColorRGB seaIce = new ColorRGB(0.80f, 0.85f, 0.90f);
        public ColorRGB beach = new ColorRGB(0.72f, 0.66f, 0.48f);
        public ColorRGB desert = new ColorRGB(0.76f, 0.63f, 0.42f);
        public ColorRGB grassland = new ColorRGB(0.28f, 0.40f, 0.16f);
        public ColorRGB forest = new ColorRGB(0.11f, 0.24f, 0.09f);
        public ColorRGB tundra = new ColorRGB(0.42f, 0.41f, 0.35f);
        public ColorRGB rock = new ColorRGB(0.36f, 0.33f, 0.30f);
        public ColorRGB snow = new ColorRGB(0.90f, 0.92f, 0.95f);

        // ---- Atmosphere & clouds (visual) ----
        /// <summary>Multiplies the Rayleigh colour of the sky (1,1,1 = physical coefficients unchanged).</summary>
        public ColorRGB atmosphereTint = new ColorRGB(1f, 1f, 1f);
        /// <summary>Fraction of the planet under cloud, 0..1 (visual: the cloud deck; ignored without an atmosphere).</summary>
        public double cloudCoverage = 0.45;
        /// <summary>Albedo of the clouds, sRGB (white for water droplets, cream for sulphuric acid).</summary>
        public ColorRGB cloudTint = new ColorRGB(1f, 1f, 1f);

        public FlattenZone[] flattenZones = new FlattenZone[0];

        // ---- Terrestrial climate modifiers (0 = off; keep existing worlds unchanged) ----
        /// <summary>0..1: dries the equatorial belt (Hadley-cell deserts).</summary>
        public double equatorialAridity;
        /// <summary>Extra mountain mask along coasts at low latitudes (0 = none).</summary>
        public double coastalMountainBoost;

        // ---- Rocky / airless style (used when surfaceStyle = Rocky) ----
        public SurfaceStyle surfaceStyle = SurfaceStyle.Terrestrial;
        /// <summary>Colour of basins / "seas" (low terrain).</summary>
        public ColorRGB lowland = new ColorRGB(0.33f, 0.30f, 0.27f);
        /// <summary>Colour of highlands.</summary>
        public ColorRGB highland = new ColorRGB(0.52f, 0.49f, 0.44f);
        /// <summary>Colour of patchy deposits (e.g. iron rust).</summary>
        public ColorRGB stain = new ColorRGB(0.45f, 0.20f, 0.12f);
        /// <summary>0 = no stains … 1 = everywhere.</summary>
        public double stainCoverage;
        public double stainFrequency = 6.0;
        /// <summary>How much darker crater floors are (0..1).</summary>
        public double craterFloorDarkening = 0.25;
        /// <summary>Hand-placed named features (always present, in addition to random craters).</summary>
        public FeatureCrater[] featureCraters = new FeatureCrater[0];
        /// <summary>Hand-placed canyons (Rocky style; colour darkened by <see cref="canyonDarkening"/>).</summary>
        public FeatureCanyon[] featureCanyons = new FeatureCanyon[0];
        /// <summary>How much darker canyon floors are (0..1).</summary>
        public double canyonDarkening = 0.25;

        // ---- Gas giant bands (used when surfaceStyle = GasGiant) ----
        /// <summary>Number of belt/zone pairs from pole to pole.</summary>
        public double bandCount = 7.0;
        /// <summary>How strongly turbulence bends the band edges [rad of band phase]; 0 = ruler-straight bands.</summary>
        public double bandTurbulence = 1.2;
        /// <summary>East–west stretch of the turbulence (streaks along the bands): latitude frequency / longitude frequency.</summary>
        public double bandStretch = 8.0;

        // ---- Artist colour map (equirectangular image of the whole body; empty = fully procedural) ----
        /// <summary>
        /// Key of the colour map in <see cref="SurfaceAlbedoMaps"/> (in Unity: Resources/PlanetMaps/&lt;key&gt;). Empty = none.
        /// If the map is not registered the surface falls back to the procedural colours above.
        /// </summary>
        public string albedoMap = "";
        /// <summary>0..1: how much the generator's own brightness variation (noise, crater floors) stays on top of the map.</summary>
        public double albedoMapDetail = 0.35;
        /// <summary>
        /// Relief taken from the map's large-scale brightness [m per unit of relative brightness]: dark regions ("seas")
        /// become basins. 0 = the map changes colour only.
        /// </summary>
        public double albedoMapRelief;
        /// <summary>
        /// Terrestrial bodies with an ocean: &gt; 0 makes the map decide land and sea (violet = water, see
        /// <see cref="SurfaceAlbedoMap.WaterScore"/>). It is the weight of the map's land signal in the continent field; the
        /// generator's noise only roughens the coastline below the map's pixel size. 0 = the noise decides (the map gives colour only).
        /// </summary>
        public double albedoMapCoastline;

        public bool UsesAlbedoMap => !string.IsNullOrEmpty(albedoMap);

        public PlanetGenerationSettings Clone()
        {
            var c = (PlanetGenerationSettings)MemberwiseClone();
            c.flattenZones = flattenZones != null ? (FlattenZone[])flattenZones.Clone() : new FlattenZone[0];
            c.featureCraters = featureCraters != null ? (FeatureCrater[])featureCraters.Clone() : new FeatureCrater[0];
            c.featureCanyons = featureCanyons != null ? (FeatureCanyon[])featureCanyons.Clone() : new FeatureCanyon[0];
            return c;
        }

        /// <summary>Clamps values to ranges the generator supports (protects against inspector typos).</summary>
        public void Sanitize()
        {
            continentOctaves = Clamp(continentOctaves, 1, 16);
            mountainOctaves = Clamp(mountainOctaves, 1, 16);
            detailOctaves = Clamp(detailOctaves, 0, 16);
            craterCount = Clamp(craterCount, 0, 2000);
            lacunarity = MathD.Clamp(lacunarity, 1.1, 4.0);
            persistence = MathD.Clamp(persistence, 0.05, 0.95);
            craterMinRadius = Math.Max(1.0, craterMinRadius);
            craterMaxRadius = Math.Max(craterMinRadius, craterMaxRadius);
            if (flattenZones == null) flattenZones = new FlattenZone[0];
            if (featureCraters == null) featureCraters = new FeatureCrater[0];
            if (featureCanyons == null) featureCanyons = new FeatureCanyon[0];
            if (albedoMap == null) albedoMap = "";
            albedoMapDetail = MathD.Clamp01(albedoMapDetail);
            canyonDarkening = MathD.Clamp01(canyonDarkening);
            bandCount = MathD.Clamp(bandCount, 0.0, 64.0);
            bandTurbulence = MathD.Clamp(bandTurbulence, 0.0, 10.0);
            bandStretch = MathD.Clamp(bandStretch, 1.0, 50.0);
            stainCoverage = MathD.Clamp01(stainCoverage);
            craterFloorDarkening = MathD.Clamp01(craterFloorDarkening);
        }

        static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
