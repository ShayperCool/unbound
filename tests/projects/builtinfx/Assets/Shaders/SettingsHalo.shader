Shader "Probe/Settings/Halo"
{
    // A custom halo shader: each halo adds (0, 0.3, 0) over its quad.
    SubShader
    {
        Tags { "RenderType" = "Overlay" }
        ZWrite Off Cull Off
        Blend One One
        ColorMask RGB
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata_t { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_t v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            float4 frag(v2f i) : SV_Target { return float4(0, 0.3, 0, 0); }
            ENDCG
        }
    }
}
