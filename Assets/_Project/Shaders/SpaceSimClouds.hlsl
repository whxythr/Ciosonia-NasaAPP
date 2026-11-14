#ifndef SPACESIM_CLOUDS_INCLUDED
#define SPACESIM_CLOUDS_INCLUDED

// Density of the cloud deck: a planet-wide weather map (coverage, type) gives the large forms, the tiling 3D noise carves them.
// Requires SPACESIM_PLANET_UNIFORMS, SpaceSimLighting.hlsl and the cloud constants of Clouds.shader.

float SSRemap(float v, float a, float b, float c, float d)
{
    return c + (v - a) * (d - c) / (b - a);
}

// Vertical density profile. type 0 = thin stratus low in the deck, 1 = towering cumulus.
float SSCloudProfile(float hf, float type)
{
    float top = lerp(0.30, 1.0, type);
    float bottom = smoothstep(0.0, 0.05 + 0.10 * type, hf);
    float upper = 1.0 - smoothstep(top * 0.5, top, hf);
    return bottom * upper;
}

// q = noise coordinate [m], hf = height in the deck (0..1), w = weather sample. detail = 1 adds the fine erosion of the edges.
float SSCloudDensity(float3 q, float hf, float4 w, float lodBase, float lodDetail, bool detail)
{
    float profile = SSCloudProfile(hf, w.g);
    float cov = w.r * _CloudCoverageBoost;
    if (cov * profile < 0.01) return 0.0;

    // Far away (a pixel spans many noise texels) the mip chain averages the noise towards its mean, and the coverage threshold
    // below then erased the clouds wholesale: decks vanished when the planet was seen from afar or at a grazing angle, and popped
    // back as the camera moved (mip changes). Past a few mips the noise is replaced by its statistical effect: the weather map's
    // own cloud systems (tens of km per texel) with a density that grows with their coverage.
    float farW = saturate((lodBase - 3.0) / 2.0);
    // Its density is chosen so a vertical column through the deck lets through 1 − 0.6·c of the light (c ≈ the cloud fraction the
    // near-field noise would show there): broken decks stay see-through from orbit instead of turning into opaque white sheets.
    float cEff = smoothstep(0.2, 1.0, cov);
    float densFar = -log(1.0 - 0.6 * cEff) / (_CloudExtinction * max(_SpaceSimCloudTop - _SpaceSimCloudBase, 1.0) * 0.5) * profile;
    if (farW >= 1.0) return densFar;

    // The large-scale warp breaks the repeat of the noise tile where the weather map is uniform (overcast decks).
    q.xz += (w.ba - 0.5) * 14000.0;
    float4 b = SAMPLE_TEXTURE3D_LOD(_SpaceSimNoise3D, sampler_SpaceSimNoise3D, q / SPACESIM_CLOUD_BASE_TILE, min(lodBase, 5.0));
    float fbm = b.g * 0.625 + b.b * 0.25 + b.a * 0.125;
    float base = saturate(SSRemap(b.r, fbm - 1.0, 1.0, 0.0, 1.0));
    float dens = saturate(SSRemap(base * profile, 1.0 - cov, 1.0, 0.0, 1.0)) * cov;
    dens = lerp(dens, densFar, farW);
    if (dens <= 0.0 || !detail || lodDetail >= 2.5 || farW > 0.0) return dens;

    float3 dq = q / SPACESIM_CLOUD_DETAIL_TILE;
    float3 n = SAMPLE_TEXTURE3D_LOD(_SpaceSimNoise3D, sampler_SpaceSimNoise3D, dq, lodDetail).gba;
    float fine = n.r * 0.625 + n.g * 0.25 + n.b * 0.125;
    float wisp = lerp(fine, 1.0 - fine, saturate(hf * 5.0)); // wispy bottoms, billowing tops
    float eroded = saturate(SSRemap(dens, wisp * 0.38, 1.0, 0.0, 1.0));
    return lerp(eroded, dens, saturate(lodDetail - 1.5)); // fades out before it is finer than a pixel (no sparkle)
}

// The stretch of a ray inside the deck. Looking up from below: from the bottom sphere to the top sphere. From above: from the top
// sphere to the bottom sphere (or back out). Inside: from the eye. A ray that meets the planet first has no cloud.
bool SSCloudSegment(float3 o, float3 d, float h0, float hb, float ht, out float t0, out float t1)
{
    float Rg = _SpaceSimRadiusM;
    t0 = 0.0;
    t1 = 0.0;
    float b = dot(o, d);
    float discT = b * b - (h0 - ht) * (2.0 * Rg + h0 + ht);
    if (discT <= 0.0) return false;
    float sT = sqrt(discT);
    float topFar = -b + sT;
    if (topFar <= 0.0) return false;
    float discB = b * b - (h0 - hb) * (2.0 * Rg + h0 + hb);
    bool hitsBottom = discB > 0.0;
    float sB = hitsBottom ? sqrt(discB) : 0.0;
    float bottomNear = -b - sB;
    float bottomFar = -b + sB;

    if (h0 > ht)
    {
        t0 = -b - sT;
        if (t0 <= 0.0) return false;
        t1 = hitsBottom && bottomNear > 0.0 ? bottomNear : topFar;
    }
    else if (h0 >= hb)
    {
        t1 = hitsBottom && bottomNear > 0.0 ? bottomNear : topFar;
    }
    else
    {
        t0 = max(bottomFar, 0.0);
        t1 = topFar;
        float discG = b * b - h0 * (2.0 * Rg + h0);
        if (discG > 0.0 && -b - sqrt(discG) > 0.0) return false; // the ground is in the way
    }
    return t1 > t0;
}

// Optical depth (density x length) towards the Sun: a few samples with growing steps, no detail noise.
float SSCloudLightDepth(float3 p, float3 q, float3 L, float3 Lcloud, float hb, float ht, float lod, float3x3 rot)
{
    float Rg = _SpaceSimRadiusM;
    float thickness = ht - hb;
    float len = thickness * 0.05;
    float s = len * 0.5;
    float depth = 0.0;
    int n = (int)_CloudLightSteps;
    [loop]
    for (int k = 0; k < n; k++)
    {
        float3 ps = p + L * s;
        float r = length(ps);
        float hf = (r - Rg - hb) / thickness;
        if (hf >= 1.0) break;
        if (hf > 0.0)
        {
            float4 w = SSSampleWeather(mul(rot, ps / r), 2.0);
            if (w.r > 0.01) depth += SSCloudDensity(q + Lcloud * s, hf, w, lod + k * 0.6, 8.0, false) * len;
        }
        s += len * 1.4;
        len *= 1.9;
    }
    return depth;
}

#endif
