// Diffuse light of the galaxy the star system lives in (our "Milky Way"): billions of unresolved stars along the galactic
// plane, a brighter, warmer bulge towards the galactic centre, dark dust lanes along the mid-plane and a few faint reddish
// emission nebulae. Purely procedural (3D value noise of the view direction), no textures.
// Drawn like the star field: a sphere of directions projected with the camera rotation only (w = 0), so it sits at infinity
// and is fixed in the inertial frame. Additive, Background queue; the atmosphere pass dims it by transmittance and the
// controller fades it with the same eye-adaptation visibility as the stars (invisible in daylight air).
Shader "SpaceSim/MilkyWay"
{
    Properties
    {
        _Brightness ("Brightness (visibility × intensity)", Float) = 0.06
        _GalPole ("Galactic north pole (world)", Vector) = (0.44, 0.47, 0.76, 0)
        _GalCenter ("Direction to the galactic centre (world)", Vector) = (0.88, -0.36, -0.30, 0)
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
                float4 _GalPole;
                float4 _GalCenter;
            CBUFFER_END

            struct Attributes { float4 vertex : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 dir = normalize(v.vertex.xyz);
                float4 clip = mul(UNITY_MATRIX_VP, float4(dir, 0.0)); // rotation only: at infinity
            #if UNITY_REVERSED_Z
                clip.z = 1e-6 * clip.w;
            #else
                clip.z = 0.999999 * clip.w;
            #endif
                o.positionCS = clip;
                o.dir = dir;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Fbm(float3 p, int octaves)
            {
                float sum = 0.0, amp = 0.5, norm = 0.0;
                for (int k = 0; k < octaves; k++)
                {
                    sum += amp * Noise(p);
                    norm += amp;
                    p = p * 2.03 + float3(17.1, 3.7, 9.2);
                    amp *= 0.5;
                }
                return sum / norm;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 pole = normalize(_GalPole.xyz);
                float3 c1 = normalize(_GalCenter.xyz - pole * dot(_GalCenter.xyz, pole));
                float3 c2 = cross(pole, c1);
                float b = asin(clamp(dot(d, pole), -1.0, 1.0));      // galactic latitude [rad]
                float l = atan2(dot(d, c2), dot(d, c1));            // galactic longitude [rad], 0 = centre

                // Thin disc seen edge-on: the band thickens and brightens towards the centre, plus the bulge.
                float towardsCentre = exp(-l * l / (2.0 * 0.9 * 0.9));
                float width = 0.075 + 0.07 * towardsCentre;
                float disc = exp(-(b * b) / (width * width)) * (0.35 + 0.65 * towardsCentre);
                float bulge = exp(-(l * l + 4.0 * b * b) / (2.0 * 0.22 * 0.22));

                // Star clouds: the band is patchy, not a smooth stripe.
                float clouds = Fbm(d * 6.0, 5);
                float clumps = saturate(0.15 + 2.2 * (clouds - 0.38));
                clumps *= clumps;
                // Mottling of countless faint, unresolved stars. Smooth multi-octave noise on a rotated lattice (a single
                // sharpened octave showed the noise grid as square blobs); the point-like detail comes from the real stars.
                const float3x3 rot = float3x3(0.80, 0.36, -0.48, -0.60, 0.48, -0.64, 0.00, 0.80, 0.60);
                float grain = 0.9 * pow(Fbm(mul(rot, d) * 48.0 + 3.1, 3), 2.0);
                float intensity = disc * (0.25 + clumps) * (0.55 + grain) + 0.9 * bulge * (0.6 + 0.6 * clouds);

                // Dust lanes along the mid-plane (the "Great Rift"): wavy, broken, darkest near b = 0.
                float wobble = 0.02 * sin(l * 3.0 + 1.3) + 0.012 * sin(l * 7.0);
                float lane = exp(-pow((b - wobble) / (0.32 * width + 0.012), 2.0));
                float dust = smoothstep(0.38, 0.72, Fbm(d * 11.0 + 5.3, 5)) * lane;
                intensity *= 1.0 - 0.85 * dust;

                // A faint glow everywhere (the halo of faint stars far from the plane), so the sky is not pure black.
                intensity += 0.02 * (0.5 + clouds);

                float3 young = float3(0.72, 0.80, 1.00);  // disc: blue-white young stars
                float3 old = float3(1.00, 0.84, 0.62);    // bulge: old, yellowish stars
                float3 color = lerp(young, old, saturate(0.25 + 0.75 * towardsCentre * exp(-b * b / 0.02))) * intensity;

                // Emission nebulae (hydrogen-alpha, reddish) sprinkled along the disc.
                float neb = smoothstep(0.66, 0.86, Fbm(d * 18.0 + 11.7, 4)) * disc;
                color += float3(1.0, 0.30, 0.38) * neb * 0.35;

                // Tiny dither: the gradients are very dark, avoid 8-bit banding after tonemapping.
                float n = Hash(float3(i.positionCS.xy, 0.37)) - 0.5;
                color = max(0.0, color * _Brightness + n * (1.0 / 1024.0) * step(1e-5, _Brightness));
                return half4(color, 0.0);
            }
            ENDHLSL
        }
    }
}
