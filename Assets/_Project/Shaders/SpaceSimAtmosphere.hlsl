#ifndef SPACESIM_ATMOSPHERE_INCLUDED
#define SPACESIM_ATMOSPHERE_INCLUDED

// Single scattering along a view ray (Rayleigh + Mie + optional high haze). Sunlight at each sample comes from the shared light model
// (the transmittance LUT replaces the nested light ray march), so a sample costs one texture read and the same sky is affordable with
// more samples. Used by the sky pass and, with fewer samples, by the clouds for the air between them and the camera.
// Requires SPACESIM_PLANET_UNIFORMS + SPACESIM_ATMOSPHERE_UNIFORMS and SpaceSimLighting.hlsl.

// Distance [m] from the camera to the opaque scene along unit ray d; 'sky' when the ray reaches nothing.
float SSSceneDistance(float2 screenUV, float3 d, out bool sky)
{
    float raw = SampleSceneDepth(screenUV);
#if UNITY_REVERSED_Z
    sky = raw <= 1e-7;
#else
    sky = raw >= 1.0 - 1e-7;
#endif
    if (sky) return 1e12;
    float3 camForward = -UNITY_MATRIX_V[2].xyz;
    // Depth is in (possibly scaled) Unity units; the ray march works in metres.
    return LinearEyeDepth(raw, _ZBufferParams) / max(dot(d, camForward), 1e-4) / max(_SpaceSimRenderScale, 1e-12);
}

// Segment of a camera ray inside the atmosphere shell, ending at the planet sphere. False if the ray misses it.
bool SSAtmosphereSegment(float3 o, float3 d, out float t0, out float t1)
{
    float R = _SpaceSimRadiusM;
    float Ha = _SpaceSimShellM;
    float h = _CameraHeight;
    t0 = 0.0;
    t1 = 0.0;
    // |o|^2 - r^2 is formed as (h - H)(2R + h + H) to avoid catastrophic cancellation at 6e6 m.
    float b = dot(o, d);
    float discA = b * b - (h - Ha) * (2.0 * R + h + Ha);
    if (discA <= 0.0) return false;
    float sA = sqrt(discA);
    t0 = max(0.0, -b - sA);
    t1 = -b + sA;
    if (t1 <= 0.0) return false;
    float discP = b * b - h * (2.0 * R + h);
    if (discP > 0.0)
    {
        float tP = -b - sqrt(discP);
        if (tP > 0.0) t1 = min(t1, tP);
    }
    return t1 > t0;
}

// Air between t0 and t1 along the camera ray: light scattered towards the camera and the transmittance of that stretch.
// 'jitter' (0..1) shifts the samples per pixel: a little noise instead of bands.
void SSIntegrateAtmosphere(float3 o, float3 d, float t0, float t1, int steps, float jitter, out float3 inscatter, out float3 transmittance)
{
    float R = _SpaceSimRadiusM;
    float3 L = _SpaceSimSunDir;
    float ds = (t1 - t0) / steps;
    float odR = 0.0, odM = 0.0, odH = 0.0;
    float3 sumR = 0.0, sumM = 0.0, sumH = 0.0, sumFill = 0.0;

    [loop]
    for (int s = 0; s < steps; s++)
    {
        float3 p = o + d * (t0 + (s + jitter) * ds);
        float rp = length(p);
        float hp = rp - R;
        float dR = exp(-hp / _ScaleHeightR) * ds;
        float dM = exp(-hp / _ScaleHeightM) * ds;
        float zh = (hp - _HazeAltitude) / _HazeThickness;
        float dH = exp(-zh * zh) * ds;
        odR += 0.5 * dR;
        odM += 0.5 * dM;
        odH += 0.5 * dH;

        float3 view = exp(-(_BetaRayleigh * odR + _MieExtinction * odM + _HazeScattering * odH));
        float sunMu = dot(p, L) / rp;
        float3 att = view * SSSunVisibility(hp, sunMu) * SSTransmittanceToTop(hp, sunMu);
        sumR += att * dR;
        sumM += att * dM;
        sumH += att * dH;
        // Higher orders of scattering, as an isotropic fill: they soften twilight and keep shadowed air from going black.
        sumFill += view * (_BetaRayleigh * dR + _BetaMie * dM + _HazeScattering * dH) * smoothstep(-0.25, 0.25, sunMu);

        odR += 0.5 * dR;
        odM += 0.5 * dM;
        odH += 0.5 * dH;
    }

    float mu = dot(d, L);
    inscatter = _SunIntensity * ((sumR * _BetaRayleigh + sumH * _HazeScattering) * SSPhaseRayleigh(mu) + sumM * _BetaMie * SSPhaseMie(mu, _MieG));
    inscatter += sumFill * _SunIntensity * _MultiScatter * 0.025;
    inscatter *= _Exposure;
    transmittance = exp(-(_BetaRayleigh * odR + _MieExtinction * odM + _HazeScattering * odH));
}

#endif
