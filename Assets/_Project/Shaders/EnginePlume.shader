// Engine exhaust plume: an emissive volume ray-marched inside a proxy cylinder (object space: y = 0 at the nozzle, y = 1 at the tip,
// radius 1 at the widest point of the plume). Everything about its shape comes from the script: exit radius and spread from the ambient
// pressure, Mach diamonds in dense air, colours and brightness from the engine type and the throttle. Purely visual: additive and HDR,
// so the bloom of the post-processing stack picks it up.
Shader "SpaceSim/EnginePlume"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1
        _CoreColor ("Core colour (HDR)", Vector) = (1.0, 0.82, 0.55, 1)
        _EdgeColor ("Edge colour (HDR)", Vector) = (1.0, 0.42, 0.12, 1)
        _ExitRadius ("Exit radius (of the widest)", Float) = 0.5
        _Diamonds ("Mach diamonds 0..1", Float) = 1
        _DiamondCount ("Mach diamonds count", Float) = 6
        _Falloff ("Length falloff", Float) = 1.3
        _Turbulence ("Turbulence 0..1", Float) = 1
        _RadiusWorld ("Exit radius [world]", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Front
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _EdgeColor;
                float _Intensity;
                float _ExitRadius;
                float _Diamonds;
                float _DiamondCount;
                float _Falloff;
                float _Turbulence;
                float _RadiusWorld;
            CBUFFER_END

            TEXTURE3D(_SpaceSimNoise3D);
            SAMPLER(sampler_SpaceSimNoise3D);

            #define STEPS 10

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionOS = v.positionOS.xyz;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float3 Emission(float3 q, float time)
            {
                float y = q.y;
                float rho = length(q.xz);
                float radius = lerp(_ExitRadius, 1.0, pow(max(y, 0.0), 0.8));

                float near = pow(saturate(1.0 - y * 1.25), 1.5);
                float wave = cos(6.2831853 * y * _DiamondCount);
                float diamond = _Diamonds * near * pow(saturate(0.5 + 0.5 * wave), 4.0);
                float coreRadius = radius * (0.5 + 0.22 * wave * _Diamonds * near);

                float n = SAMPLE_TEXTURE3D_LOD(_SpaceSimNoise3D, sampler_SpaceSimNoise3D, float3(q.x * 1.3, y * 3.0 - time * 2.6, q.z * 1.3), 0.0).r;
                float turb = lerp(1.0, 0.35 + 1.5 * n, _Turbulence * saturate(y * 1.5 + 0.1));

                float a = rho / max(coreRadius, 1e-3);
                float e = rho / max(radius, 1e-3);
                float core = exp(-a * a * 1.7);
                float sheath = exp(-e * e * 1.4) * turb;
                float along = pow(saturate(1.0 - y), _Falloff);
                return (_CoreColor.rgb * core * (1.0 + 3.5 * diamond) + _EdgeColor.rgb * sheath) * along;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 cameraWS = GetCameraPositionWS();
                float3 cameraOS = TransformWorldToObject(cameraWS);
                float3 dd = i.positionOS - cameraOS; // t = 1 on the back face

                // The ray inside the unit cylinder 0 <= y <= 1.
                float t0 = 0.0, t1 = 1.0;
                float a = dot(dd.xz, dd.xz);
                float b = 2.0 * dot(cameraOS.xz, dd.xz);
                float c = dot(cameraOS.xz, cameraOS.xz) - 1.0;
                if (a > 1e-9)
                {
                    float disc = b * b - 4.0 * a * c;
                    if (disc <= 0.0) return half4(0, 0, 0, 0);
                    float s = sqrt(disc);
                    t0 = max(t0, (-b - s) / (2.0 * a));
                    t1 = min(t1, (-b + s) / (2.0 * a));
                }
                else if (c > 0.0) return half4(0, 0, 0, 0);
                if (abs(dd.y) > 1e-6)
                {
                    float ty0 = -cameraOS.y / dd.y;
                    float ty1 = (1.0 - cameraOS.y) / dd.y;
                    t0 = max(t0, min(ty0, ty1));
                    t1 = min(t1, max(ty0, ty1));
                }
                else if (cameraOS.y < 0.0 || cameraOS.y > 1.0) return half4(0, 0, 0, 0);

                // Stop at opaque geometry (rocket, pad, ground): soft contact without a separate pass.
                float3 toPoint = i.positionWS - cameraWS;
                float worldLength = length(toPoint);
                float raw = SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS));
                float cosine = max(dot(toPoint / worldLength, -UNITY_MATRIX_V[2].xyz), 1e-4);
                float sceneT = LinearEyeDepth(raw, _ZBufferParams) / cosine / worldLength;
                t1 = min(t1, sceneT);
                if (t1 <= t0) return half4(0, 0, 0, 0);

                float time = _Time.y;
                float jitter = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));
                float dt = (t1 - t0) / STEPS;
                float ds = dt * worldLength / max(_RadiusWorld, 1e-3);
                float3 sum = 0.0;
                [loop]
                for (int s = 0; s < STEPS; s++)
                    sum += Emission(cameraOS + dd * (t0 + (s + jitter) * dt), time) * ds;

                return half4(sum * _Intensity * 0.25, 0.0);
            }
            ENDHLSL
        }
    }
}
