Shader "Probe/Settings/ScreenSpaceShadows"
{
    // A custom screen-space shadow collector: attenuation 0.3 everywhere.
    SubShader
    {
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata_t { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_t v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            float4 frag(v2f i) : SV_Target { return 0.3; }
            ENDCG
        }
    }
    Fallback Off
}
