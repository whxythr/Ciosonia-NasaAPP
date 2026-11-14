#ifndef SPACESIM_LIGHTING_INCLUDED
#define SPACESIM_LIGHTING_INCLUDED

// The one light model of SpaceSim. Terrain, clouds, atmosphere and exhaust smoke all take their sunlight and sky light from here,
// so a sunset reddens all of them by the same amount and the terminator falls in the same place.
//
//  * Sunlight at a point: star colour x planet shadow (soft, by the true horizon of the sphere at that height)
//    x atmospheric transmittance from a LUT built from the PHYSICAL density profile (AtmosphereOptics.Transmittance, the same
//    function that dims the real Unity light in SolarLightingController).
//  * Sky light: the planet's own ambient colour, fading through twilight.
//
// The including shader must be URP (Core.hlsl first) and put SPACESIM_PLANET_UNIFORMS (SpaceSimUniforms.hlsl) inside its UnityPerMaterial
// CBUFFER before including this file.
//
// All heights are metres above the reference radius; every radial difference is computed from a height rather than from two big
// radii, because at 6e6 m a float radius has ~0.5 m of noise and its square has no useful low digits at all.

// Size of the transmittance LUT (must match AtmosphereTransmittanceLut in C#).
#define SPACESIM_LUT_WIDTH 128.0
#define SPACESIM_LUT_HEIGHT 32.0
// Period of the camera-relative cloud noise coordinates [m] (CloudLayerRenderer.NoiseWrap).
#define SPACESIM_CLOUD_BASE_TILE 24000.0
#define SPACESIM_CLOUD_DETAIL_TILE 3000.0

TEXTURE2D(_SpaceSimTransmittance);
SAMPLER(sampler_SpaceSimTransmittance);
TEXTURE3D(_SpaceSimNoise3D);
SAMPLER(sampler_SpaceSimNoise3D);
TEXTURE2D(_SpaceSimCloudMap);
SAMPLER(sampler_SpaceSimCloudMap);

// ---- Colour ----------------------------------------------------------------------------------------------------------------

float3 SpaceSimToLinear(float3 c)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return c;
#else
    return c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878); // fast sRGB -> linear
#endif
}

float SSLuminance(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// Screen-door noise: turns the banding of a coarse ray march into a fine, stable grain.
float SSInterleavedGradientNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// ---- Scattering phase functions ----------------------------------------------------------------------------------------------

float SSPhaseRayleigh(float mu) { return 3.0 / (16.0 * PI) * (1.0 + mu * mu); }

// Cornette-Shanks: Mie aerosols in the sky.
float SSPhaseMie(float mu, float g)
{
    float g2 = g * g;
    float denom = pow(max(1.0 + g2 - 2.0 * g * mu, 1e-4), 1.5);
    return 3.0 / (8.0 * PI) * ((1.0 - g2) * (1.0 + mu * mu)) / ((2.0 + g2) * denom);
}

// Henyey-Greenstein: droplets in clouds and smoke.
float SSPhaseHG(float mu, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * mu, 1e-4), 1.5));
}

// ---- Sunlight -----------------------------------------------------------------------------------------------------------------

// Distance [m] from height h to the top of the visual atmosphere, along a ray with cosine of zenith angle mu.
float SSDistanceToTop(float h, float mu)
{
    float Rg = _SpaceSimRadiusM;
    float Ha = _SpaceSimShellM;
    float r = Rg + h;
    float c = (Ha - h) * (2.0 * Rg + h + Ha); // Rt^2 - r^2 without cancellation
    float s = sqrt(max(c + r * r * mu * mu, 0.0));
    return mu >= 0.0 ? c / max(r * mu + s, 1e-3) : s - r * mu;
}

// Bruneton's non-linear mapping: resolves the horizon, where the transmittance changes fastest.
float2 SSTransmittanceUV(float h, float mu)
{
    float Rg = _SpaceSimRadiusM;
    float Ha = _SpaceSimShellM;
    float H = sqrt(Ha * (2.0 * Rg + Ha));
    float rho = sqrt(h * (2.0 * Rg + h));
    float d = SSDistanceToTop(h, mu);
    float dMin = Ha - h;
    float dMax = rho + H;
    float xMu = saturate((d - dMin) / max(dMax - dMin, 1e-3));
    float xR = saturate(rho / max(H, 1e-3));
    return float2(0.5 / SPACESIM_LUT_WIDTH + xMu * (1.0 - 1.0 / SPACESIM_LUT_WIDTH),
                  0.5 / SPACESIM_LUT_HEIGHT + xR * (1.0 - 1.0 / SPACESIM_LUT_HEIGHT));
}

// Spectral transmittance from height h to space along cos(zenith) = mu. Below the horizon the LUT holds the tangent ray.
float3 SSTransmittanceToTop(float h, float mu)
{
    if (_SpaceSimHasAtmosphere < 0.5) return float3(1.0, 1.0, 1.0);
    return SAMPLE_TEXTURE2D_LOD(_SpaceSimTransmittance, sampler_SpaceSimTransmittance, SSTransmittanceUV(max(h, 0.0), mu), 0.0).rgb;
}

// 1 where the star is above the true horizon of the sphere at height h, 0 in the planet's shadow; soft over the star's disk.
float SSSunVisibility(float h, float mu)
{
    float Rg = _SpaceSimRadiusM;
    float hc = max(h, 0.0);
    float cosHorizon = -sqrt(hc * (2.0 * Rg + hc)) / (Rg + hc);
    return smoothstep(-0.012, 0.012, mu - cosHorizon);
}

// Sunlight arriving at height h for a star at cos(zenith) = mu (same units as a lit Lambert surface of albedo 1).
float3 SSSunLight(float h, float mu)
{
    return _SpaceSimSunColor * SSSunVisibility(h, mu) * SSTransmittanceToTop(h, mu);
}

// Light from the whole sky at height h: dims through twilight, thins out with altitude.
float3 SSSkyLight(float h, float mu)
{
    if (_SpaceSimHasAtmosphere < 0.5) return float3(0.0, 0.0, 0.0);
    float day = smoothstep(-0.20, 0.14, mu);
    day *= day;
    float3 T = SSTransmittanceToTop(h, max(mu, 0.04));
    float peak = max(T.r, max(T.g, T.b));
    float3 tint = lerp(float3(1.0, 1.0, 1.0), T / max(peak, 1e-4), 0.3); // the low Sun warms what it still lights
    float column = exp(-max(h, 0.0) / max(_SpaceSimAirScaleHeight, 1.0));
    return _SpaceSimSkyAmbient * day * tint * lerp(0.15, 1.0, column);
}

// ---- Cloud weather map (shared by the cloud shader and the cloud shadows on the ground) ---------------------------------------

// r = coverage, g = cloud type (0 stratus .. 1 towering), ba = large-scale noise warp. Equirectangular: x = longitude, y = latitude.
float4 SSSampleWeather(float3 dirCloud, float lod)
{
    float2 uv = float2(atan2(dirCloud.z, dirCloud.x) * 0.15915494 + 0.5, asin(clamp(dirCloud.y, -1.0, 1.0)) * 0.31830989 + 0.5);
    if (lod < 1.0)
    {
        // Close up the texels are tens of km wide: a smooth (C1) interpolation instead of the bilinear one, whose straight contour
        // segments read as stair steps along the edges of a deck.
        float2 size;
        _SpaceSimCloudMap.GetDimensions(size.x, size.y);
        float2 st = uv * size - 0.5;
        float2 cell = floor(st);
        float2 f = st - cell;
        f = f * f * (3.0 - 2.0 * f);
        uv = (cell + f + 0.5) / size;
    }
    return SAMPLE_TEXTURE2D_LOD(_SpaceSimCloudMap, sampler_SpaceSimCloudMap, uv, lod);
}

// Sunlight that gets through the cloud layer to a ground point: the point's shadow lands where the sun ray crosses the layer.
// 1 = clear, 0 = fully shadowed. Two texture reads and no marching: the deck is treated as one sheet at its mid height.
float SSCloudShadow(float3 positionWS, float3 cameraWS)
{
    if (_SpaceSimCloudShadow <= 0.0) return 1.0;
    float3 L = _SpaceSimSunDir;
    float3 rel = (positionWS - _SpaceSimPlanetCenter) / max(_SpaceSimRenderScale, 1e-12);
    float r = length(rel);
    float cosZ = dot(rel, L) / r;
    if (cosZ <= 0.02) return 1.0; // dark or almost: no shadow to speak of
    float h = r - _SpaceSimRadiusM;
    float mid = 0.5 * (_SpaceSimCloudBase + _SpaceSimCloudTop);
    if (h >= mid) return 1.0;
    float t = (mid - h) / max(cosZ, 0.08);
    float3x3 rot = SS_CLOUD_ROTATION;
    float cov = SSSampleWeather(normalize(mul(rot, rel + L * t)), 2.0).r;
    if (cov <= 0.02) return 1.0;
    float3 q = _SpaceSimCloudNoiseOrigin + mul(rot, (positionWS - cameraWS) / max(_SpaceSimRenderScale, 1e-12) + L * t);
    float n = SAMPLE_TEXTURE3D_LOD(_SpaceSimNoise3D, sampler_SpaceSimNoise3D, q / SPACESIM_CLOUD_BASE_TILE, 1.5).r;
    float opaque = smoothstep(1.0 - cov + 0.1, 1.0 - cov + 0.45, n) * smoothstep(0.05, 0.45, cov);
    return 1.0 - _SpaceSimCloudShadow * opaque;
}

// ---- Surface ------------------------------------------------------------------------------------------------------------------

// Ocean: wave-broadened sun glitter (GGX), sky mirrored at grazing angles (Fresnel), and the water body seen through it.
// N is the wave normal; dist widens the glitter with distance, where single waves merge into a slope distribution.
float3 SSShadeWater(float3 body, float3 N, float3 up, float3 V, float3 L, float3 sun, float3 sky, float ambientFloor, float glitter, float dist)
{
    float NdotV = saturate(dot(N, V));
    float fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);

    float roughness = lerp(0.07, 0.24, saturate(dist / 4000.0));
    float a2 = roughness * roughness;
    float3 H = normalize(L + V);
    float NdotH = saturate(dot(N, H));
    float denom = NdotH * NdotH * (a2 - 1.0) + 1.0;
    float ggx = a2 / (PI * denom * denom);
    float vis = 0.25 / max(lerp(dot(H, V) * dot(H, V), 1.0, a2), 0.02);
    float3 spec = sun * ggx * vis * saturate(dot(up, L)) * fresnel * glitter;

    // Sky radiance towards the mirrored direction: brighter near the horizon.
    float3 skyColour = sky * (1.2 + 1.2 * pow(1.0 - saturate(dot(reflect(-V, N), up)), 3.0));
    float3 water = body * (sun * (0.45 + 0.55 * saturate(dot(up, L))) * 0.8 + sky) + body * ambientFloor;
    return lerp(water, skyColour, fresnel) + spec;
}

// Terrain or ocean. waterMask = 1 for water; shadow = real-time shadow attenuation; cloudShadow = SSCloudShadow.
// waveNormalWS is used only for water, dist is the distance to the camera [Unity units].
float3 SpaceSimShadeSurface(float3 positionWS, float3 normalWS, float3 waveNormalWS, float3 albedoSRGB, float waterMask,
                            float3 viewDirWS, float shadow, float cloudShadow, float ambientFloor, float waterSpecular, float dist)
{
    float3 albedo = SpaceSimToLinear(albedoSRGB);
    float3 toCenter = positionWS - _SpaceSimPlanetCenter;
    float r = length(toCenter);
    float3 up = toCenter / max(r, 1e-6);
    float h = (r - _SpaceSimPlanetRadius) / max(_SpaceSimRenderScale, 1e-12); // metres, independent of scale
    float3 L = _SpaceSimSunDir;
    float mu = dot(up, L);

    float3 sun = SSSunLight(h, mu) * shadow * cloudShadow;
    float3 sky = SSSkyLight(h, mu) * lerp(0.65, 1.0, cloudShadow); // an overcast sky lights the ground too, but less

    // Sea ice is water by mask but behaves like snow.
    bool openWater = waterMask > 0.5 && SSLuminance(albedoSRGB) < 0.45;
    if (openWater)
        return SSShadeWater(albedo, waveNormalWS, up, viewDirWS, L, sun, sky, ambientFloor, waterSpecular, dist);

    float ndl = saturate(dot(normalWS, L));
    float skyView = saturate(0.5 + 0.5 * dot(normalWS, up));
    return albedo * (sun * ndl + sky * skyView + ambientFloor);
}

#endif
