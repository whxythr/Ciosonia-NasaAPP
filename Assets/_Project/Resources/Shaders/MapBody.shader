// A planet, moon or star on the 3D map. Albedo comes from vertex colours (the same procedural biomes as the terrain).
// Lit by the star at _SpaceSimMapStar (map space); a thin rim stands for the atmosphere. Lives in Resources so builds include it.
Shader "SpaceSim/MapBody"
{
    Properties
    {
        _Color ("Tint / star colour", Color) = (1, 1, 1, 1)
        _Emissive ("Emissive (star)", Float) = 0
        _Rim ("Atmosphere rim (rgb, a = strength)", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Emissive;
            float4 _Rim;
            float3 _SpaceSimMapStar;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                if (_Emissive > 0.5) return float4(_Color.rgb, 1);
                float3 n = normalize(i.normal);
                float3 l = normalize(_SpaceSimMapStar - i.worldPos);
                float3 view = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 c = i.color.rgb;
                float3 albedo = c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878); // sRGB → linear
                float diffuse = dot(n, l);
                float3 lit = albedo * _Color.rgb * (0.025 + 1.25 * saturate(diffuse));
                float rim = pow(1.0 - saturate(dot(n, view)), 3.0) * saturate(diffuse + 0.35);
                return float4(lit + _Rim.rgb * rim * _Rim.a, 1);
            }
            ENDCG
        }
    }
}
