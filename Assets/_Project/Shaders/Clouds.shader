// Volumetric cloud deck of one planet: ray-marched per pixel on a shell (same principle and same code path from the ground, through the
// deck and from orbit). Sunlight and sky light come from the shared model; the air between the camera and the cloud is integrated here,
// so the result is  cloud seen through air  and it replaces what the sky pass already drew behind it (premultiplied alpha).
// Noise coordinates are camera-relative (see CloudLayerRenderer), so nothing swims at planetary distances.
Shader "SpaceSim/Clouds"
{
    Properties
    {
        _CloudColor ("Cloud albedo", Color) = (1, 1, 1, 1)
        _CloudExtinction ("Extinction [1/m]", Float) = 0.012
        _CloudCoverageBoost ("Coverage multiplier", Float) = 1
        _CloudSteps ("Primary steps", Float) = 32
        _CloudLightSteps ("Light steps", Float) = 4
        _CloudAmbient ("Ambient", Float) = 2.2
        _CloudFade ("Fade", Float) = 1
        _CloudMapTexel ("Weather texel [m]", Float) = 20000
        _CloudCirrus ("Cirrus strength", Float) = 0.5
        _CloudCirrusHeight ("Cirrus height [m]", Float) = 11000
        [HideInInspector] _SpaceSimSunDir ("", Vector) = (1, 0, 0, 0)
        [HideInInspector] _SpaceSimSunColor ("", Vector) = (1, 1, 1, 0)
        [HideInInspector] _SpaceSimPlanetCenter ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpaceSimPlanetRadius ("", Float) = 6371000
        [HideInInspector] _SpaceSimRenderScale ("", Float) = 1
        [HideInInspector] _SpaceSimHasAtmosphere ("", Float) = 0
        [HideInInspector] _SpaceSimSkyAmbient ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpaceSimRadiusM ("", Float) = 6371000
        [HideInInspector] _SpaceSimShellM ("", Float) = 100000
        [HideInInspector] _SpaceSimAirScaleHeight ("", Float) = 8500
        [HideInInspector] _SpaceSimCloudShadow ("", Float) = 0
        [HideInInspector] _SpaceSimCloudBase ("", Float) = 1500
        [HideInInspector] _SpaceSimCloudTop ("", Float) = 9000
        [HideInInspector] _SpaceSimCloudNoiseOrigin ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpaceSimCloudRow0 ("", Vector) = (1, 0, 0, 0)
        [HideInInspector] _SpaceSimCloudRow1 ("", Vector) = (0, 1, 0, 0)
        [HideInInspector] _SpaceSimCloudRow2 ("", Vector) = (0, 0, 1, 0)
        [HideInInspector] _CameraPlanetPos ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _CameraHeight ("", Float) = 0
        [HideInInspector] _BetaRayleigh ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BetaMie ("", Float) = 0
        [HideInInspector] _HazeScattering ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _MieExtinction ("", Float) = 0
        [HideInInspector] _ScaleHeightR ("", Float) = 8500
        [HideInInspector] _ScaleHeightM ("", Float) = 1200
        [HideInInspector] _MieG ("", Float) = 0.8
        [HideInInspector] _SunIntensity ("", Float) = 22
        [HideInInspector] _Exposure ("", Float) = 1
        [HideInInspector] _MultiScatter ("", Float) = 1
        [HideInInspector] _HazeAltitude ("", Float) = 70000
        [HideInInspector] _HazeThickness ("", Float) = 8000
        [NoScaleOffset] _SpaceSimTransmittance ("Transmittance LUT", 2D) = "white" {}
        [NoScaleOffset] _SpaceSimCloudMap ("Weather map", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-5" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Cull Front
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "SpaceSimUniforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
                SPACESIM_PLANET_UNIFORMS
                SPACESIM_ATMOSPHERE_UNIFORMS
                float4 _CloudColor;
                float _CloudExtinction;
                float _CloudCoverageBoost;
                float _CloudSteps;
                float _CloudLightSteps;
                float _CloudAmbient;
                float _CloudFade;
                float _CloudMapTexel;
                float _CloudCirrus;
                float _CloudCirrusHeight;
            CBUFFER_END

            #include "SpaceSimLighting.hlsl"
            #include "SpaceSimAtmosphere.hlsl"
            #include "SpaceSimClouds.hlsl"

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
            #if UNITY_REVERSED_Z
                o.positionCS.z = max(o.positionCS.z, 1e-6 * o.positionCS.w);
            #else
                o.positionCS.z = min(o.positionCS.z, 0.999999 * o.positionCS.w);
            #endif
                return o;
            }

            // March through the deck: returns the light leaving the cloud towards the camera (before the air in front of it),
            // the transmittance and the mean distance of what was seen.
            void MarchDeck(float3 o, float3 d, float3 dCloud, float tLimit, float pixelAngle, float jitter,
                           out float3 radiance, out float transmittance, out float meanT)
            {
                radiance = 0.0;
                transmittance = 1.0;
                meanT = 0.0;

                float hb = _SpaceSimCloudBase, ht = _SpaceSimCloudTop;
                float t0, t1;
                if (!SSCloudSegment(o, d, _CameraHeight, hb, ht, t0, t1)) return;
                t1 = min(t1, tLimit);
                if (t1 <= t0) return;

                float Rg = _SpaceSimRadiusM;
                float thickness = ht - hb;
                float3x3 rot = SS_CLOUD_ROTATION;
                float3 L = _SpaceSimSunDir;
                float3 Lcloud = mul(rot, L);
                float mu = dot(d, L);

                // Steps stay short near the camera and grow along long, grazing paths.
                int steps = (int)_CloudSteps;
                float len = t1 - t0;
                float minStep = max(thickness / 26.0, 100.0);
                float first = minStep, growth = 0.0;
                if (len <= steps * minStep) first = len / steps;
                else growth = (len / minStep - steps) * 2.0 / (steps * (steps - 1.0));

                // Phase of three scattering orders, once per ray.
                float3 phase;
                phase.x = lerp(SSPhaseHG(mu, 0.78), SSPhaseHG(mu, -0.25), 0.35);
                phase.y = lerp(SSPhaseHG(mu, 0.45), SSPhaseHG(mu, -0.15), 0.35);
                phase.z = lerp(SSPhaseHG(mu, 0.25), SSPhaseHG(mu, -0.08), 0.35);
                phase *= 4.0 * PI;

                float3 sun = 0.0, sky = 0.0;
                bool lit = false;
                float t = t0 + jitter * first;
                float seen = 0.0, seenT = 0.0;

                [loop]
                for (int i = 0; i < steps; i++)
                {
                    if (t >= t1 || transmittance < 0.015) break;
                    float dt = first * (1.0 + growth * i);
                    float3 p = o + d * t;
                    float r = length(p);
                    float h = r - Rg;
                    float hf = (h - hb) / thickness;
                    if (hf > 0.0 && hf < 1.0)
                    {
                        float footprint = max(t, 1.0) * pixelAngle;
                        float lodW = max(log2(footprint / _CloudMapTexel), 0.0);
                        float4 w = SSSampleWeather(mul(rot, p / r), lodW);
                        if (w.r > 0.01)
                        {
                            float3 q = _SpaceSimCloudNoiseOrigin + dCloud * t;
                            float lodBase = max(log2(footprint * 64.0 / SPACESIM_CLOUD_BASE_TILE) + 1.6, 0.0); // a little softer than the pixel: no sparkle from orbit
                            float lodDetail = max(log2(footprint * 64.0 / SPACESIM_CLOUD_DETAIL_TILE), 0.0);
                            float dens = SSCloudDensity(q, hf, w, lodBase, lodDetail, true);
                            if (dens > 0.003)
                            {
                                if (!lit)
                                {
                                    float sunMu = dot(p, L) / r;
                                    sun = SSSunLight(h, sunMu) * _CloudColor.rgb;
                                    sky = (SSSkyLight(h, sunMu) * _CloudAmbient + _SpaceSimSunColor * 0.0015) * _CloudColor.rgb;
                                    lit = true;
                                }
                                float tau = _CloudExtinction * SSCloudLightDepth(p, q, L, Lcloud, hb, ht, lodBase + 1.0, rot);
                                float3 sunTerm = sun * (exp(-tau) * phase.x + 0.55 * exp(-tau * 0.35) * phase.y + 0.3 * exp(-tau * 0.12) * phase.z);
                                sunTerm *= lerp(1.0, 1.0 - exp(-tau * 2.0 - dens * 3.0), 0.55 * saturate(1.0 - mu)); // dark creases away from the Sun
                                float3 skyTerm = sky * lerp(0.45, 1.0, hf) * (1.0 - 0.4 * dens);

                                float tr = exp(-_CloudExtinction * dens * dt);
                                float absorbed = transmittance * (1.0 - tr);
                                radiance += absorbed * (sunTerm + skyTerm);
                                seen += absorbed;
                                seenT += absorbed * t;
                                transmittance *= tr;
                            }
                        }
                    }
                    t += dt;
                }
                if (seen > 0.0) meanT = seenT / seen;
            }

            // High thin ice cloud: a single textured sheet, streaked along the jet direction.
            void MarchCirrus(float3 o, float3 d, float3 dCloud, float tLimit, float pixelAngle, out float3 radiance, out float alpha, out float t)
            {
                radiance = 0.0;
                alpha = 0.0;
                t = 0.0;
                if (_CloudCirrus <= 0.0) return;
                float Rg = _SpaceSimRadiusM;
                float h0 = _CameraHeight;
                float hc = _CloudCirrusHeight;
                float b = dot(o, d);
                float disc = b * b - (h0 - hc) * (2.0 * Rg + h0 + hc);
                if (disc <= 0.0) return;
                float s = sqrt(disc);
                t = h0 > hc ? -b - s : -b + s;
                if (t <= 0.0 || t >= tLimit) return;
                float discG = b * b - h0 * (2.0 * Rg + h0);
                if (discG > 0.0 && -b - sqrt(discG) > 0.0 && h0 <= hc) return; // below the sheet, looking at the ground

                float3 p = o + d * t;
                float r = length(p);
                float3 L = _SpaceSimSunDir;
                float footprint = max(t, 1.0) * pixelAngle;
                float lod = max(log2(footprint * 64.0 / 24000.0), 0.0);
                float3 q = _SpaceSimCloudNoiseOrigin + dCloud * t;
                float n1 = SAMPLE_TEXTURE3D_LOD(_SpaceSimNoise3D, sampler_SpaceSimNoise3D, q / float3(96000.0, 24000.0, 24000.0), lod).b;
                float n2 = SAMPLE_TEXTURE3D_LOD(_SpaceSimNoise3D, sampler_SpaceSimNoise3D, q / float3(24000.0, 12000.0, 12000.0), lod).a;
                float4 w = SSSampleWeather(mul(SS_CLOUD_ROTATION, p / r), 3.0);
                float field = n1 * 0.7 + n2 * 0.3;
                float a = saturate((field - 0.56) / 0.26) * _CloudCirrus * smoothstep(0.0, 0.6, w.g * 0.6 + 0.4 * w.r);
                if (a <= 0.002) return;

                float sunMu = dot(p, L) / r;
                float h = r - Rg;
                float mu = dot(d, L);
                float phase = lerp(SSPhaseHG(mu, 0.8), 0.08, 0.5) * 4.0 * PI;
                radiance = (SSSunLight(h, sunMu) * phase + SSSkyLight(h, sunMu) * _CloudAmbient * 0.5) * _CloudColor.rgb * a;
                alpha = a;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.positionWS - GetCameraPositionWS());
                float3 o = _CameraPlanetPos;
                float3 dCloud = mul(SS_CLOUD_ROTATION, d);
                float pixelAngle = 2.0 / (abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y);
                float jitter = 0.5 + 0.65 * (frac(sin(dot(i.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453) - 0.5); // white, partial: less grain, no stripes

                bool sky;
                float tLimit = SSSceneDistance(GetNormalizedScreenSpaceUV(i.positionCS), d, sky);

                float3 deck;
                float deckT, deckMean;
                MarchDeck(o, d, dCloud, tLimit, pixelAngle, jitter, deck, deckT, deckMean);
                float deckA = 1.0 - deckT;

                float3 cirrus;
                float cirrusA, cirrusT;
                MarchCirrus(o, d, dCloud, tLimit, pixelAngle, cirrus, cirrusA, cirrusT);

                if (deckA <= 0.002 && cirrusA <= 0.002) return float4(0.0, 0.0, 0.0, 0.0);

                // The air in front of each layer: the sky pass already drew the whole column, the cloud has to replace it.
                float tAir0, tAir1;
                if (SSAtmosphereSegment(o, d, tAir0, tAir1))
                {
                    float3 inscatter, tr;
                    if (deckA > 0.002 && deckMean > tAir0)
                    {
                        SSIntegrateAtmosphere(o, d, tAir0, min(deckMean, tAir1), 6, 0.5, inscatter, tr);
                        deck = deck * tr + inscatter * deckA;
                    }
                    if (cirrusA > 0.002 && cirrusT > tAir0)
                    {
                        SSIntegrateAtmosphere(o, d, tAir0, min(cirrusT, tAir1), 4, 0.5, inscatter, tr);
                        cirrus = cirrus * tr + inscatter * cirrusA;
                    }
                }

                float3 rgb;
                float alpha;
                if (_CameraHeight < _CloudCirrusHeight)
                {
                    rgb = deck + cirrus * (1.0 - deckA);
                    alpha = 1.0 - (1.0 - deckA) * (1.0 - cirrusA);
                }
                else
                {
                    rgb = cirrus + deck * (1.0 - cirrusA);
                    alpha = 1.0 - (1.0 - deckA) * (1.0 - cirrusA);
                }
                return float4(rgb, alpha) * _CloudFade;
            }
            ENDHLSL
        }
    }
}
