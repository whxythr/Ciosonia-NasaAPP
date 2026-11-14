// The Sun's disk (true angular size) plus a small glow, at infinity in the Sun's direction. The disk is far above 1 in the HDR buffer, so
// the bloom of the post-processing stack spreads it. Behind the atmosphere it is attenuated by the atmosphere pass like any other background.
Shader "SpaceSim/SunDisk"
{
    Properties
    {
        _SunDir ("Sun direction", Vector) = (1, 0, 0, 0)
        _AngularRadius ("Angular radius [rad]", Float) = 0.004653
        _GlowScale ("Glow extent (× radius)", Float) = 6
        _DiskIntensity ("Disk intensity", Float) = 30
        _GlowIntensity ("Glow intensity", Float) = 0.6
        _Color ("Colour", Color) = (1, 0.98, 0.94, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background+1" "RenderType" = "Background" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }

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
                float4 _SunDir;
                float4 _Color;
                float _AngularRadius;
                float _GlowScale;
                float _DiskIntensity;
                float _GlowIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 s = normalize(_SunDir.xyz);
                float3 helper = abs(s.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 r = normalize(cross(helper, s));
                float3 u = cross(s, r);
                float ext = tan(_AngularRadius * _GlowScale);
                float3 dir = normalize(s + (r * v.uv.x + u * v.uv.y) * ext);
                float4 clip = mul(UNITY_MATRIX_VP, float4(dir, 0.0));
            #if UNITY_REVERSED_Z
                clip.z = 1e-6 * clip.w;
            #else
                clip.z = 0.999999 * clip.w;
            #endif
                o.positionCS = clip;
                o.uv = v.uv * _GlowScale; // in units of the disk radius
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float rr = length(i.uv);
                float disk = 1.0 - smoothstep(0.92, 1.0, rr);
                float mu = sqrt(saturate(1.0 - rr * rr));
                float limb = 0.4 + 0.6 * mu;                   // limb darkening
                float glow = exp(-max(rr - 1.0, 0.0) * 1.5) * (1.0 - disk) * step(rr, _GlowScale);
                float3 c = _Color.rgb * (disk * limb * _DiskIntensity + glow * _GlowIntensity);
                return half4(c, 0.0);
            }
            ENDHLSL
        }
    }
}
