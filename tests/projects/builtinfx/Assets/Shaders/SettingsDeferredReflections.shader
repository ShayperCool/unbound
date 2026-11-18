Shader "Probe/Settings/DeferredReflections"
{
    // A custom deferred reflections shader: each probe pass adds (0.4, 0, 0.4), the final pass (0, 0.25, 0.25).
    Properties { _SrcBlend ("", Float) = 1 _DstBlend ("", Float) = 1 }
    SubShader
    {
        Pass
        {
            ZWrite Off ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata_t { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_t v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            float4 frag(v2f i) : SV_Target { return float4(0.4, 0, 0.4, 0); }
            ENDCG
        }
        Pass
        {
            ZWrite Off ZTest Always
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata_t { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_t v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            float4 frag(v2f i) : SV_Target { return float4(0, 0.25, 0.25, 0); }
            ENDCG
        }
    }
    Fallback Off
}
