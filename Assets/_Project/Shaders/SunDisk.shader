// The star as the eye and a camera see it, at infinity in its direction: the disk (true angular size, limb darkening), a bright
// aureole hugging it, a wide soft halo (forward scattering in the air and in the eye/lens), and radial rays (diffraction spikes and
// glare streaks) that slowly turn and flicker. Angles are absolute (radians), so the halo and rays keep their size when the disk is
// tiny (outer planets). The disk is far above 1 in the HDR buffer, so bloom spreads it further. Behind the atmosphere it is
// attenuated by the atmosphere pass like any other background; clouds and solid objects cover it.
Shader "SpaceSim/SunDisk"
{
    Properties
    {
        _SunDir ("Sun direction", Vector) = (1, 0, 0, 0)
        _AngularRadius ("Angular radius [rad]", Float) = 0.004653
        _HaloAngle ("Extent of halo and rays [rad]", Float) = 0.45
        _DiskIntensity ("Disk intensity", Float) = 30
        _GlowIntensity ("Aureole intensity", Float) = 2.5
        _HaloIntensity ("Wide halo intensity", Float) = 0.35
        _RayIntensity ("Ray intensity", Float) = 0.6
        _RayCount ("Number of main rays", Float) = 6
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
                float _HaloAngle;
                float _DiskIntensity;
                float _GlowIntensity;
                float _HaloIntensity;
                float _RayIntensity;
                float _RayCount;
            CBUFFER_END

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            float Extent() { return min(max(_AngularRadius * 1.3, _HaloAngle), 1.2); }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 s = normalize(_SunDir.xyz);
                float3 helper = abs(s.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 r = normalize(cross(helper, s));
                float3 u = cross(s, r);
                float3 dir = s + (r * v.uv.x + u * v.uv.y) * tan(Extent());
                float4 clip = mul(UNITY_MATRIX_VP, float4(dir, 0.0));
            #if UNITY_REVERSED_Z
                clip.z = 1e-6 * clip.w;
            #else
                clip.z = 0.999999 * clip.w;
            #endif
                o.positionCS = clip;
                o.dir = dir; // linear on the quad's plane; normalised per pixel, the angles are exact
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 s = normalize(_SunDir.xyz);
                float3 d = normalize(i.dir);
                float angle = acos(clamp(dot(d, s), -1.0, 1.0));  // from the centre of the disk [rad]
                float rr = angle / max(_AngularRadius, 1e-6);       // in disk radii

                // Disk with limb darkening (antialiased edge).
                float disk = 1.0 - smoothstep(0.92, 1.0, rr);
                float mu = sqrt(saturate(1.0 - rr * rr));
                float limb = 0.4 + 0.6 * mu;

                // Aureole: steep, a few disk radii (never thinner than ~0.6°, so a distant star still glows).
                float aureole = exp(-max(angle - _AngularRadius, 0.0) / max(_AngularRadius * 1.6, 0.010));
                // Wide halo: Lorentzian-like falloff to the edge of the quad.
                float halo = 1.0 / (1.0 + pow(angle / max(_AngularRadius * 3.0, 0.02), 2.0));

                // Rays: position angle around the Sun, in a frame fixed to the Sun direction.
                float3 helper = abs(s.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 rx = normalize(cross(helper, s));
                float3 ry = cross(s, rx);
                float phi = atan2(dot(d, ry), dot(d, rx));
                float t = _Time.y;
                float n = max(_RayCount, 1.0);
                float spikes = pow(abs(cos(phi * n * 0.5 + t * 0.03)), 60.0);                 // thin main spikes
                float streaks = pow(abs(cos(phi * n * 1.5 - t * 0.05 + 0.7)), 16.0) * 0.45     // finer secondary rays
                              + pow(abs(sin(phi * 13.0 + t * 0.11)), 30.0) * 0.25;
                float flicker = 0.85 + 0.15 * sin(t * 1.7 + phi * 5.0);
                float rayFall = saturate(1.0 - angle / (_HaloAngle * 0.9));
                float rays = (spikes + streaks * flicker) * rayFall * rayFall
                           * smoothstep(_AngularRadius * 0.9, _AngularRadius * 1.6, angle);

                float edge = saturate(1.0 - angle / Extent());        // the quad's border never shows
                float3 c = _Color.rgb * (disk * limb * _DiskIntensity
                                         + (aureole * _GlowIntensity + halo * _HaloIntensity) * (1.0 - disk) * edge * edge
                                         + rays * _RayIntensity * (1.0 - disk));
                return half4(c, 0.0);
            }
            ENDHLSL
        }
    }
}
