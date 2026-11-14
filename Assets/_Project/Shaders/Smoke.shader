// Exhaust smoke and steam: camera-facing soft particles drawn in one instanced call (RocketExhaustEffects fills the buffer).
// Each puff is a noisy ball of density, lit by the shared light model per particle; it fades where it meets the opaque scene and when the
// camera is inside it, and takes on the air between itself and the camera. Premultiplied alpha, so it can also glow near the flame.
Shader "SpaceSim/Smoke"
{
    Properties
    {
        _SoftDistance ("Soft contact (fraction of radius)", Float) = 0.45
        _GlowRange ("Flame glow range [world]", Float) = 60
        [HideInInspector] _GlowPosition ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _GlowColor ("", Vector) = (0, 0, 0, 0)
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
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "SpaceSimUniforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
                SPACESIM_PLANET_UNIFORMS
                SPACESIM_ATMOSPHERE_UNIFORMS
                float3 _GlowPosition;
                float _GlowRange;
                float3 _GlowColor;
                float _SoftDistance;
            CBUFFER_END

            #include "SpaceSimLighting.hlsl"

            struct SmokeParticle
            {
                float4 positionSize; // xyz world, w radius
                float4 misc;         // x age 0..1, y seed 0..1, z opacity, w thickness (0 thin steam .. 1 dense)
                float4 color;        // rgb albedo (linear), a soot (darkens with age inverse)
            };
            StructuredBuffer<SmokeParticle> _Particles;

            struct Attributes
            {
                float4 positionOS : POSITION; // quad corner -1..1
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;  // camera-aligned -1..1
                float3 noiseUV : TEXCOORD1;
                float4 params : TEXCOORD2;  // x age, y opacity, z eye depth, w radius
                float3 albedo : TEXCOORD3;
                float3 sun : TEXCOORD4;
                float3 sky : TEXCOORD5;
                float3 glow : TEXCOORD6;
                float3 inscatter : TEXCOORD7;
                float3 transmit : TEXCOORD8;
                float3 lightView : TEXCOORD9; // sun direction in the billboard's basis (x right, y up, z towards the camera)
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                SmokeParticle p = _Particles[v.instanceID];
                float3 center = p.positionSize.xyz;
                float radius = p.positionSize.w;
                float age = p.misc.x;
                float seed = p.misc.y;

                float angle = seed * 6.2831853 + age * (seed - 0.5) * 2.0;
                float ca = cos(angle), sa = sin(angle);
                float2 corner = float2(v.positionOS.x * ca - v.positionOS.y * sa, v.positionOS.x * sa + v.positionOS.y * ca);
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 towardsCamera = UNITY_MATRIX_V[2].xyz;
                float3 positionWS = center + (right * corner.x + up * corner.y) * radius;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.corner = corner;
                o.noiseUV = float3(v.positionOS.xy * (0.42 + 0.12 * seed) + seed * 37.0, seed * 11.0 + age * 0.35);
                o.params = float4(age, p.misc.z, -TransformWorldToView(positionWS).z, radius);
                o.albedo = p.color.rgb * lerp(1.0, 0.35, p.color.a * (1.0 - age));

                // One light estimate per particle: it costs a few texture reads per puff instead of per pixel.
                float3 rel = (center - _SpaceSimPlanetCenter) / max(_SpaceSimRenderScale, 1e-12);
                float r = length(rel);
                float h = r - _SpaceSimRadiusM;
                float3 L = _SpaceSimSunDir;
                float mu = dot(rel, L) / r;
                o.sun = SSSunLight(h, mu);
                o.sky = SSSkyLight(h, mu);
                o.lightView = float3(dot(L, right), dot(L, up), dot(L, towardsCamera));

                float gd = length(center - _GlowPosition) / max(_GlowRange, 1e-3);
                float g = saturate(1.0 - gd);
                o.glow = _GlowColor * g * g * lerp(1.0, 0.3, age);

                // Air between the puff and the camera, assuming the density half way along.
                float dist = length(GetCameraPositionWS() - center) / max(_SpaceSimRenderScale, 1e-12);
                float hm = max(0.5 * (h + _CameraHeight), 0.0);
                float3 tau = (_BetaRayleigh * exp(-hm / _ScaleHeightR) + _MieExtinction * exp(-hm / _ScaleHeightM)) * dist;
                o.transmit = exp(-tau);
                o.inscatter = (1.0 - o.transmit) * (o.sky * 1.4 + o.sun * 0.08);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.corner, i.corner);
                if (r2 >= 1.0) discard;
                float sphere = sqrt(1.0 - r2);
                float age = i.params.x;
                float radius = i.params.w;
                float eye = i.params.z;

                // Two octaves of billows: lumps inside the puff and a ragged rim instead of a disc.
                float4 n = SAMPLE_TEXTURE3D_LOD(_SpaceSimNoise3D, sampler_SpaceSimNoise3D, i.noiseUV, 0.0);
                float4 m = SAMPLE_TEXTURE3D_LOD(_SpaceSimNoise3D, sampler_SpaceSimNoise3D, i.noiseUV * 2.7 + 0.37, 0.0);
                float billow = n.r * 0.65 + m.g * 0.35;
                float erode = lerp(0.30, 0.62, age);
                float density = saturate((sqrt(sphere) * (0.35 + billow * 1.1) - erode) / (1.0 - erode));
                density *= density * (3.0 - 2.0 * density);
                if (density <= 0.002) discard;

                // Fake volume: the ball's normal bent by the noise.
                float2 bend = i.corner + (n.gb - 0.5) * 1.6 + (m.ba - 0.5) * 0.8;
                float3 N = normalize(float3(bend, max(sphere, 0.25)));
                float ndl = dot(N, i.lightView);
                float wrap = saturate(ndl * 0.55 + 0.45);
                float shade = lerp(0.72, 1.0, saturate(N.y * 0.5 + 0.5));
                float forward = saturate(-i.lightView.z);
                forward *= forward * forward * forward;
                float3 color = i.albedo * (i.sun * (wrap + 0.8 * forward) + i.sky * 1.6 * shade) + i.glow * (1.0 - 0.5 * sphere);

                // Fade where it touches opaque geometry and when the camera is inside it.
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float soft = saturate((sceneEye - eye) / max(radius * _SoftDistance, 0.05));
                float nearFade = saturate((eye - radius * 0.15) / max(radius * 0.8, 0.05));

                float alpha = density * i.params.y * soft * nearFade;
                color = color * i.transmit + i.inscatter;
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
