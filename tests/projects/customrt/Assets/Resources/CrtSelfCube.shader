// Update shader for cubemaps: the previous content (_SelfTextureCube) plus _Add.
Shader "CustomRT/SelfCube"
{
    Properties { _Add ("Add", Vector) = (1, 0, 0, 0) }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #include "UnityCustomRenderTexture.cginc"
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            float4 _Add;
            float4 frag(v2f_customrendertexture IN) : SV_Target { return texCUBE(_SelfTextureCube, IN.direction) + _Add; }
            ENDCG
        }
    }
}
