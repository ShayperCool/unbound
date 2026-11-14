// Framebuffer fetch (Metal): the documented fragment signature with an inout color target where
// UNITY_FRAMEBUFFER_FETCH_AVAILABLE is defined. The fetch path writes half the color it reads
// plus 0.125; the other path magenta. Alpha: 0.5 on the fetch path, else 0.25, plus 0.125 with
// UNITY_FRAMEBUFFER_FETCH_ENABLED and 0.0625 with UNITY_COMPILER_HLSLCC.
Shader "Probe/Fetch/Plain"
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
            static const half Flags = 0
            #if defined(UNITY_FRAMEBUFFER_FETCH_ENABLED)
                + 0.125
            #endif
            #if defined(UNITY_COMPILER_HLSLCC)
                + 0.0625
            #endif
                ;
            #ifdef UNITY_FRAMEBUFFER_FETCH_AVAILABLE
            void frag(float4 pos : SV_POSITION, inout half4 ocol : SV_Target)
            {
                ocol = half4(ocol.rgb * 0.5 + 0.125, 0.5 + Flags);
            }
            #else
            half4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                return half4(1, 0, 1, 0.25 + Flags);
            }
            #endif
            ENDCG
        }
    }
}
