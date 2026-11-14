// Procedural close-range surface detail from the tiling 3D noise texture (SpaceSimNoiseTextures): no UV unwrap, so no seams and no
// triplanar blending. Coordinates are planet-fixed metres modulo SPACESIM_DETAIL_PERIOD (see CoordinateSystem.FillMesh) and every tile
// divides that period exactly, so chunks join seamlessly. The tile sizes are unrelated to each other to hide the repeat of each layer.
// Far away the texture's mip chain averages every layer to its mean: detail fades by itself, without a visible distance band.
#ifndef SPACESIM_DETAIL_INCLUDED
#define SPACESIM_DETAIL_INCLUDED

#define SPACESIM_DETAIL_PERIOD 4096.0

// Tile sizes [m], each 4096 / an integer.
#define SS_TILE_PATCH  (SPACESIM_DETAIL_PERIOD / 5.0)
#define SS_TILE_CLUMP  (SPACESIM_DETAIL_PERIOD / 53.0)
#define SS_TILE_STONE  (SPACESIM_DETAIL_PERIOD / 541.0)
#define SS_TILE_GRAIN  (SPACESIM_DETAIL_PERIOD / 4243.0)
#define SS_TILE_WAVE1  (SPACESIM_DETAIL_PERIOD / 97.0)
#define SS_TILE_WAVE2  (SPACESIM_DETAIL_PERIOD / 613.0)

float4 SSDetailNoise(float3 fixedPos, float tile)
{
    return SAMPLE_TEXTURE3D(_SpaceSimNoise3D, sampler_SpaceSimNoise3D, fixedPos / tile);
}

// Slope, height and close-range variation of a land surface. 'fixedPos' = planet-fixed position mod period, 'normal' = OBJECT-space
// (planet-fixed axes) normal, 'up' = object-space local vertical, 'h' = height above the reference radius [m], 'dist' = distance to camera.
void SpaceSimGroundDetail(float3 fixedPos, float3 up, float h, float dist, inout float3 albedo, inout float3 normal)
{
    // Colour by slope and height works at any distance: it only needs the (interpolated) terrain normal.
    float slope = 1.0 - saturate(dot(normal, up));
    float lum = dot(albedo, float3(0.333, 0.333, 0.334));
    float neutral = 1.0 - saturate((max(albedo.r, max(albedo.g, albedo.b)) - min(albedo.r, min(albedo.g, albedo.b))) * 4.0);
    float snowLike = smoothstep(0.55, 0.75, lum) * neutral;

    float patchN = 0.5, clump = 0.5, stone = 0.5, grain = 0.5;
    if (dist < 30000.0) patchN = SSDetailNoise(fixedPos, SS_TILE_PATCH).g;
    if (dist < 3000.0) clump = SSDetailNoise(fixedPos, SS_TILE_CLUMP).g;
    if (dist < 500.0) stone = SSDetailNoise(fixedPos, SS_TILE_STONE).b;
    if (dist < 120.0) grain = SSDetailNoise(fixedPos, SS_TILE_GRAIN).a;

    float rockMask = smoothstep(0.16, 0.42, slope + (patchN - 0.5) * 0.14);
    float3 rock = lerp(float3(lum, lum, lum), albedo, 0.35) * float3(0.92, 0.88, 0.84);
    // Exposed rock shows faint horizontal strata.
    float strata = 1.0 + 0.07 * sin(h * 0.035 + patchN * 7.0);
    rock *= strata;
    // Snow does not hold on steep faces; vegetation and soil give way to rock.
    albedo = lerp(albedo, rock, rockMask * lerp(1.0, 0.9, snowLike));

    bool greenish = albedo.g > albedo.r * 1.05 && albedo.g > albedo.b;
    float amp = greenish ? 0.5 : 0.32;
    float v = (patchN - 0.5) * 0.45 + (clump - 0.5) * amp + (stone - 0.5) * 0.3 + (grain - 0.5) * 0.22;
    albedo *= saturate(1.0 + v);
    if (greenish) albedo = lerp(albedo, albedo * float3(1.1, 1.0, 0.72), saturate((patchN - 0.55) * 3.5)); // dry, yellowish patches

    // Normal from the height of the noise layers (Mikkelsen's screen-space bump mapping): needs no tangent frame.
    if (dist < 1200.0)
    {
        float fade = saturate(1.0 - dist / 1200.0);
        float height = clump * 1.1 + stone * 0.22 + grain * 0.035;
        float3 dpdx = ddx(fixedPos), dpdy = ddy(fixedPos);
        float dhdx = ddx(height) * fade, dhdy = ddy(height) * fade;
        float3 r1 = cross(dpdy, normal), r2 = cross(normal, dpdx);
        float det = dot(dpdx, r1);
        float3 gradient = (det < 0.0 ? -1.0 : 1.0) * (dhdx * r1 + dhdy * r2);
        normal = normalize(abs(det) * normal - gradient);
    }
}

// Small wind waves of open water (close range only). Far away the glitter is a statistical highlight (SSShadeWater).
float3 SpaceSimWaveNormal(float3 fixedPos, float3 up, float dist)
{
    float fade = saturate(1.0 - dist / 2500.0);
    if (fade <= 0.0) return up;
    float t = _Time.y;
    float4 a = SSDetailNoise(fixedPos + float3(t * 0.45, 0.0, t * 0.30), SS_TILE_WAVE1);
    float4 b = SSDetailNoise(fixedPos + float3(-t * 0.25, t * 0.10, t * 0.55), SS_TILE_WAVE2);
    float3 tilt = (a.gba - 0.5) * 0.30 + (b.gba - 0.5) * 0.16;
    tilt -= up * dot(tilt, up);
    return normalize(up + tilt * fade);
}

// Foam along the shore, where the per-vertex water mask falls from 1 to 0.
float SpaceSimShoreFoam(float3 fixedPos, float waterMask, float dist)
{
    if (dist > 2500.0 || waterMask > 0.985) return 0.0;
    float t = _Time.y * 0.12;
    float n = SSDetailNoise(fixedPos + float3(t, 0.0, -t * 0.7), SS_TILE_STONE).g;
    float band = smoothstep(0.985, 0.70, waterMask) * smoothstep(0.30, 0.55, waterMask);
    return saturate(band * smoothstep(0.42, 0.78, n)) * saturate(1.0 - dist / 2500.0);
}

// Vertex-shader part of the LOD transition (geomorphing): moves a vertex and blends its normal and colour towards what the parent
// chunk has at that point as the camera recedes, so the moment a chunk is replaced by its parent (or its four children) nothing changes.
// edge = chunk edge [m], split = ProceduralLodSettings.splitDistanceFactor, dist = camera distance of this vertex.
// Returns the morph factor: 0 = the chunk's own shape, 1 = exactly the parent's.
float SpaceSimLodMorph(float dist, float edge, float split)
{
    if (edge <= 0.0) return 0.0;
    return saturate((dist / (edge * split) - 1.3) / 0.45);
}

float3 SpaceSimUnpackMorphNormal(float packed)
{
    float a = floor(packed / 4096.0);
    float b = packed - a * 4096.0;
    float2 e = float2(a, b) / 4095.0 * 2.0 - 1.0;
    float3 n = float3(e.x, 1.0 - abs(e.x) - abs(e.y), e.y);
    if (n.y < 0.0)
    {
        float2 s = float2(n.x >= 0.0 ? 1.0 : -1.0, n.z >= 0.0 ? 1.0 : -1.0);
        n.xz = (1.0 - abs(n.zx)) * s;
    }
    return normalize(n);
}

float3 SpaceSimUnpackMorphColor(float packed)
{
    float r = floor(packed / 65536.0);
    float rest = packed - r * 65536.0;
    float g = floor(rest / 256.0);
    return float3(r, g, rest - g * 256.0) / 255.0;
}

#endif
