// Orbit and trajectory lines on the 3D map: every segment is a quad widened in screen space, so a line keeps its thickness in
// pixels at any zoom. POSITION = this end, (uv.xy, uv2.x) = the other end, uv2.y = side (±1). Lives in Resources so builds include it.
Shader "SpaceSim/MapLine"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Width ("Half width [px]", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Width;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            float4 vert(appdata v) : SV_POSITION
            {
                const float nearW = 1e-3;
                float4 a = UnityObjectToClipPos(float4(v.vertex.xyz, 1.0));
                float4 b = UnityObjectToClipPos(float4(v.uv.x, v.uv.y, v.uv2.x, 1.0));
                if (a.w < nearW && b.w < nearW) return float4(0.0, 0.0, 0.0, -1.0); // the whole segment is behind the camera
                // Cut a segment that crosses the camera plane, or its end would project to the opposite side of the screen.
                if (a.w < nearW) a = lerp(a, b, (nearW - a.w) / (b.w - a.w));
                if (b.w < nearW) b = lerp(b, a, (nearW - b.w) / (a.w - b.w));

                float2 along = (b.xy / b.w - a.xy / a.w) * _ScreenParams.xy;
                float length2 = dot(along, along);
                along = length2 > 1e-8 ? along / sqrt(length2) : float2(1.0, 0.0);
                a.xy += float2(-along.y, along.x) * v.uv2.y * _Width * 2.0 / _ScreenParams.xy * a.w;
                return a;
            }

            float4 frag() : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
