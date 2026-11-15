// Framebuffer fetch (Metal) squaring the color it reads: on a multisampled target the squares of
// the samples average to another color than the square of their average.
Shader "Probe/Fetch/Square"
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
            #ifdef UNITY_FRAMEBUFFER_FETCH_AVAILABLE
            void frag(float4 pos : SV_POSITION, inout half4 ocol : SV_Target)
            {
                ocol = half4(ocol.rgb * ocol.rgb, 1);
            }
            #else
            half4 frag(float4 pos : SV_POSITION) : SV_Target { return half4(1, 0, 1, 1); }
            #endif
            ENDCG
        }
    }
}
