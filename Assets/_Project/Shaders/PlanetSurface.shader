// Terrain/ocean surface of a planet or moon. Albedo comes from vertex colours (procedural biomes), alpha = water mask.
// Light comes from the shared model (SpaceSimLighting.hlsl): sunlight dimmed by the atmosphere LUT, the planet's true horizon, cloud
// shadows and URP real-time shadows near the camera. Aerial perspective is added afterwards by the atmosphere pass.
// Every planet has its own material: star direction, centre, radius and scattering are per-material values.
// LOD chunks morph towards their parent in the vertex shader, so replacing a chunk does not pop.
Shader "SpaceSim/PlanetSurface"
{
    Properties
    {
        _AmbientFloor ("Ambient floor (night)", Range(0, 0.05)) = 0.004
        _WaterSpecular ("Water specular", Range(0, 2)) = 1.0
        [HideInInspector] _LodSplitFactor ("LOD split factor", Float) = 2.2
        [HideInInspector] _SpaceSimSunDir ("Sun direction", Vector) = (1, 0, 0, 0)
        [HideInInspector] _SpaceSimSunColor ("Sun colour", Vector) = (1, 1, 1, 0)
        [HideInInspector] _SpaceSimPlanetCenter ("Planet centre", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpaceSimPlanetRadius ("Planet radius", Float) = 6371000
        [HideInInspector] _SpaceSimRenderScale ("Render scale", Float) = 1
        [HideInInspector] _SpaceSimHasAtmosphere ("Has atmosphere", Float) = 0
        [HideInInspector] _SpaceSimSkyAmbient ("Sky ambient", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpaceSimRadiusM ("Radius [m]", Float) = 6371000
        [HideInInspector] _SpaceSimShellM ("Shell height [m]", Float) = 100000
        [HideInInspector] _SpaceSimAirScaleHeight ("Scale height [m]", Float) = 8500
        [HideInInspector] _SpaceSimCloudShadow ("Cloud shadow", Float) = 0
        [HideInInspector] _SpaceSimCloudBase ("Cloud base [m]", Float) = 1500
        [HideInInspector] _SpaceSimCloudTop ("Cloud top [m]", Float) = 9000
        [HideInInspector] _SpaceSimCloudNoiseOrigin ("Cloud noise origin", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SpaceSimCloudRow0 ("", Vector) = (1, 0, 0, 0)
        [HideInInspector] _SpaceSimCloudRow1 ("", Vector) = (0, 1, 0, 0)
        [HideInInspector] _SpaceSimCloudRow2 ("", Vector) = (0, 0, 1, 0)
        [NoScaleOffset] _SpaceSimTransmittance ("Transmittance LUT", 2D) = "white" {}
        [NoScaleOffset] _SpaceSimCloudMap ("Cloud weather map", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // Shadows.hlsl (ShadowCaster pass) uses LerpWhiteTo from CommonMaterial.hlsl, which Core.hlsl does not include.
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
        #include "SpaceSimUniforms.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _AmbientFloor;
            float _WaterSpecular;
            float _LodSplitFactor;
            SPACESIM_PLANET_UNIFORMS
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SpaceSimLighting.hlsl"
            #include "SpaceSimDetail.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float3 uv0 : TEXCOORD0; // chunk centre mod period (x, y), chunk edge [m]
                float2 uv1 : TEXCOORD1; // chunk centre mod period (z), radial morph offset [m]
                float2 uv2 : TEXCOORD2; // packed parent normal, packed parent colour
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalOS : TEXCOORD1;
                float3 fixedPos : TEXCOORD2;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 up = normalize(positionWS - _SpaceSimPlanetCenter);
                float morph = SpaceSimLodMorph(length(GetCameraPositionWS() - positionWS), v.uv0.z, _LodSplitFactor);
                float3 positionOS = v.positionOS.xyz + TransformWorldToObjectDir(up) * v.uv1.y * morph;
                o.positionWS = TransformObjectToWorld(positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalOS = morph > 0.0 ? lerp(v.normalOS, SpaceSimUnpackMorphNormal(v.uv2.x), morph) : v.normalOS;
                o.fixedPos = float3(v.uv0.x, v.uv0.y, v.uv1.x) + v.positionOS.xyz;
                o.color = v.color;
                if (morph > 0.0) o.color.rgb = lerp(v.color.rgb, SpaceSimUnpackMorphColor(v.uv2.y), morph);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 cameraWS = GetCameraPositionWS();
                float3 toCamera = cameraWS - i.positionWS;
                float dist = length(toCamera);
                float3 viewDir = toCamera / max(dist, 1e-5);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float3 rel = i.positionWS - _SpaceSimPlanetCenter;
                float r = length(rel);
                float3 upWS = rel / max(r, 1e-6);
                float3 upOS = TransformWorldToObjectDir(upWS);
                float h = (r - _SpaceSimPlanetRadius) / max(_SpaceSimRenderScale, 1e-12);

                float3 albedo = i.color.rgb;
                float3 nOS = normalize(i.normalOS);
                float water = i.color.a;
                float3 waveWS = upWS;
                if (water > 0.5)
                {
                    waveWS = normalize(TransformObjectToWorldNormal(SpaceSimWaveNormal(i.fixedPos, upOS, dist)));
                    albedo = lerp(albedo, float3(0.92, 0.94, 0.95), SpaceSimShoreFoam(i.fixedPos, water, dist));
                }
                else
                {
                    SpaceSimGroundDetail(i.fixedPos, upOS, h, dist, albedo, nOS);
                }

                float cloudShadow = SSCloudShadow(i.positionWS, cameraWS);
                float3 c = SpaceSimShadeSurface(i.positionWS, normalize(TransformObjectToWorldNormal(nOS)), waveWS, albedo, water,
                                                viewDir, mainLight.shadowAttenuation, cloudShadow, _AmbientFloor, _WaterSpecular, dist);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma target 3.5
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(Attributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 3.5
            #include "SpaceSimLighting.hlsl"
            #include "SpaceSimDetail.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            // Same morph as the colour pass: a depth prepass must match it exactly.
            float4 DepthVert(Attributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 up = normalize(positionWS - _SpaceSimPlanetCenter);
                float morph = SpaceSimLodMorph(length(GetCameraPositionWS() - positionWS), v.uv0.z, _LodSplitFactor);
                return TransformObjectToHClip(v.positionOS.xyz + TransformWorldToObjectDir(up) * v.uv1.y * morph);
            }

            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
