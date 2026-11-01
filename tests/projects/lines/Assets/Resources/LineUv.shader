// Writes the interpolated first texture coordinate (x, y) unchanged.
Shader "LineProbe/Uv"
{
    SubShader
    {
        Tags { "Queue" = "Geometry" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            float4 frag(v2f i) : SV_Target { return float4(i.uv, 0, 1); }
            ENDCG
        }
    }
}
