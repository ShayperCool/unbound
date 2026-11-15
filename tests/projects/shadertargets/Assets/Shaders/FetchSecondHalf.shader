// Framebuffer fetch (Metal) of the second color target: color target 0 gets half of what target 1
// held, target 1 half of it plus 0.125.
Shader "Probe/Fetch/SecondHalf"
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
            void frag(float4 pos : SV_POSITION, out half4 first : SV_Target0, inout half4 second : SV_Target1)
            {
                first = half4(second.rgb * 0.5, 1);
                second = half4(second.rgb * 0.5 + 0.125, 1);
            }
            #else
            void frag(float4 pos : SV_POSITION, out half4 first : SV_Target0, out half4 second : SV_Target1)
            {
                first = half4(1, 0, 1, 1);
                second = half4(1, 0, 1, 1);
            }
            #endif
            ENDCG
        }
    }
}
