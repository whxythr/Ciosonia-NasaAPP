// Star field at infinity. Each star is a screen-space quad around a fixed direction in the inertial frame
// (Unity world axes), projected with the camera's rotation only — so stars never move with camera translation
// and stay fixed relative to inertial space. Drawn first (Background queue); later passes overwrite/attenuate it.
Shader "SpaceSim/Starfield"
{
    Properties
    {
        _Brightness ("Brightness (visibility)", Float) = 1
        _PixelSize ("Star size [px]", Float) = 1.6
        _Twinkle ("Scintillation 0..1", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "IgnoreProjector" = "True" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Brightness;
                float _PixelSize;
                float _Twinkle;
            CBUFFER_END

            struct Attributes
            {
                float4 vertex : POSITION;   // star direction
                float4 color : COLOR;       // colour × relative flux
                float2 uv : TEXCOORD0;      // quad corner (−1..1)
                float2 size : TEXCOORD1;    // x = size multiplier, y = scintillation phase 0..1
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 dir = normalize(v.vertex.xyz);
                float4 clip = mul(UNITY_MATRIX_VP, float4(dir, 0.0)); // w = 0: rotation only, point at infinity
            #if UNITY_REVERSED_Z
                clip.z = 1e-6 * clip.w;
            #else
                clip.z = 0.999999 * clip.w;
            #endif
                float2 pixel = 2.0 / _ScaledScreenParams.xy;
                // The quad is 1.7× wider than the visible core: room for the faint halo of the profile below.
                clip.xy += v.uv * pixel * _PixelSize * v.size.x * 1.7 * clip.w;
                o.positionCS = clip;
                // Scintillation: air turbulence makes a point source flicker a few to ~20 times a second; two incommensurate
                // waves per star, strongest near the bottom of the air column, none in space.
                float ph = v.size.y * 6.2831853;
                float t = _Time.y;
                float flicker = sin(t * (9.0 + 11.0 * v.size.y) + ph * 7.0) * sin(t * (3.1 + 4.0 * frac(v.size.y * 7.3)) + ph);
                o.color = v.color * _Brightness * 0.75 * max(0.0, 1.0 + 0.55 * _Twinkle * flicker);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                // Sharp core plus a faint halo (the optics' point-spread wings): bright stars read as bright, not just bigger.
                float a = (exp(-r2 * 9.0) + 0.08 * exp(-r2 * 2.0)) * step(r2, 1.0);
                return half4(i.color.rgb * a, 0.0);
            }
            ENDHLSL
        }
    }
}
