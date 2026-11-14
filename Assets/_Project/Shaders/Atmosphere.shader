// Single-scattering atmosphere (Rayleigh + Mie), ray-marched per pixel on a shell drawn with front-face culling,
// so the SAME computation runs whether the camera is on the ground, in the upper atmosphere or in space —
// there is no switch between a "sky" and a "space" mode, hence no visual jump at any altitude.
//
// Output is composited as  result = inscatter + scene × transmittance  (Blend One SrcAlpha), which gives the blue sky, aerial
// perspective over distant terrain, the thin limb seen from orbit, and dims the stars. The output is scene-linear HDR: the tonemapper
// of the post-processing stack is what rolls it off. Ray length is limited by the scene depth and by the planet sphere.
Shader "SpaceSim/Atmosphere"
{
    Properties
    {
        _SunIntensity ("Sun intensity", Float) = 22
        _Exposure ("Exposure", Float) = 1
        _MultiScatter ("Multiple scattering fill", Float) = 1
        [HideInInspector] _ViewSamples ("View samples", Float) = 28
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
        [HideInInspector] _HazeAltitude ("", Float) = 70000
        [HideInInspector] _HazeThickness ("", Float) = 8000
        [NoScaleOffset] _SpaceSimTransmittance ("Transmittance LUT", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Cull Front
            ZWrite Off
            ZTest Always
            Blend One SrcAlpha

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
                float _ViewSamples;
            CBUFFER_END

            #include "SpaceSimLighting.hlsl"
            #include "SpaceSimAtmosphere.hlsl"

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
                // The shell writes no depth and ignores the depth test, so its clip depth only decides clipping: keep the
                // far side inside the far plane. Near the ground the camera's far plane is ~900 km (real-time shadows need
                // it), shorter than the line of sight to the shell along the horizon.
            #if UNITY_REVERSED_Z
                o.positionCS.z = max(o.positionCS.z, 1e-6 * o.positionCS.w);
            #else
                o.positionCS.z = min(o.positionCS.z, 0.999999 * o.positionCS.w);
            #endif
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.positionWS - GetCameraPositionWS());
                float3 o = _CameraPlanetPos;

                float t0, t1;
                if (!SSAtmosphereSegment(o, d, t0, t1)) return float4(0, 0, 0, 1);

                bool sky;
                t1 = min(t1, SSSceneDistance(GetNormalizedScreenSpaceUV(i.positionCS), d, sky));
                if (t1 <= t0) return float4(0, 0, 0, 1);

                float3 inscatter, transmittance;
                float jitter = SSInterleavedGradientNoise(i.positionCS.xy);
                SSIntegrateAtmosphere(o, d, t0, t1, (int)_ViewSamples, jitter, inscatter, transmittance);
                return float4(inscatter, dot(transmittance, float3(0.3333, 0.3333, 0.3334)));
            }
            ENDHLSL
        }
    }
}
