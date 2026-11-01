// Initialization shader: Unity's initialization vertex program; _Mode 0 the texcoords and
// texture info, 1 the cube direction and face.
Shader "CustomRT/Init"
{
    Properties { _Mode ("Mode", Float) = 0 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #include "UnityCustomRenderTexture.cginc"
            #pragma vertex InitCustomRenderTextureVertexShader
            #pragma fragment frag
            float _Mode;
            float4 frag(v2f_init_customrendertexture IN) : SV_Target
            {
                return _Mode > 0.5 ? float4(IN.direction, _CustomRenderTextureInfo.w) : float4(IN.texcoord, _CustomRenderTextureInfo.x + 100 * _CustomRenderTextureInfo.z);
            }
            ENDCG
        }
    }
}
