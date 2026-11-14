// The framebuffer fetch defines of a program without an inout color target: red is 1 with
// UNITY_FRAMEBUFFER_FETCH_AVAILABLE, green with UNITY_FRAMEBUFFER_FETCH_ENABLED, blue with
// UNITY_COMPILER_HLSLCC.
Shader "Probe/Fetch/Flags"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            half4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                half4 c = half4(0, 0, 0, 1);
            #if defined(UNITY_FRAMEBUFFER_FETCH_AVAILABLE)
                c.r = 1;
            #endif
            #if defined(UNITY_FRAMEBUFFER_FETCH_ENABLED)
                c.g = 1;
            #endif
            #if defined(UNITY_COMPILER_HLSLCC)
                c.b = 1;
            #endif
                return c;
            }
            ENDCG
        }
    }
}
