// Initialization shader with two passes: the first writes (1, 0, 0, 1), the second adds (0, 10, 0, 0).
Shader "CustomRT/InitTwo"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCustomRenderTexture.cginc"
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex InitCustomRenderTextureVertexShader
            #pragma fragment frag
            float4 frag(v2f_init_customrendertexture IN) : SV_Target { return float4(1, 0, 0, 1); }
            ENDCG
        }
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex InitCustomRenderTextureVertexShader
            #pragma fragment frag
            float4 frag(v2f_init_customrendertexture IN) : SV_Target { return float4(0, 10, 0, 0); }
            ENDCG
        }
    }
}
