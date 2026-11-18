Shader "Probe/Settings/Deferred"
{
    // A custom deferred shading shader: every light adds (0.05, 0.3, 0.1); the LDR decode pass is (0.2, 0.2, 0.8).
    Properties { _SrcBlend ("", Float) = 1 _DstBlend ("", Float) = 1 }
    SubShader
    {
        Pass
        {
            ZWrite Off
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata_t { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_t v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            float4 frag(v2f i) : SV_Target { return float4(0.05, 0.3, 0.1, 0); }
            ENDCG
        }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata_t { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_t v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            float4 frag(v2f i) : SV_Target { return float4(0.2, 0.2, 0.8, 1); }
            ENDCG
        }
    }
    Fallback Off
}
