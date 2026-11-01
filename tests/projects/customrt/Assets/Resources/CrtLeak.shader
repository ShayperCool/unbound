// A blit shader reading the custom render texture variables (as globals after an update).
Shader "CustomRT/Leak"
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
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img IN) : SV_Target { return _CustomRenderTextureInfo; }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img IN) : SV_Target { return CustomRenderTextureParameters + float4(0, 0, 0, CustomRenderTextureCenters[0].x * 100 + CustomRenderTextureSizesAndRotations[0].x); }
            ENDCG
        }
    }
}
