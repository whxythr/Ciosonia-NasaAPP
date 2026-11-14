// Re-entry / ascent heating glow around the nose: additive shell whose brightness the script sets from the Sutton–Graves
// heat flux. Colour follows the equilibrium temperature (dull red → orange → yellow-white).
Shader "SpaceSim/HeatGlow"
{
    Properties
    {
        _Color ("Colour", Color) = (1.0, 0.45, 0.15, 1)
        _Intensity ("Intensity", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off
            Cull Back
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetCameraPositionWS() - positionWS;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float facing = saturate(dot(normalize(i.normalWS), normalize(i.viewWS)));
                float a = _Intensity * (0.35 * pow(facing, 0.5) + 0.65 * pow(1.0 - facing, 2.0));
                return half4(_Color.rgb * a * 1.6, a);
            }
            ENDHLSL
        }
    }
}
