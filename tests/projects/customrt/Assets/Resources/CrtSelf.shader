// Update shader: the previous content (_SelfTexture2D) plus _Add.
Shader "CustomRT/Self"
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
            float4 frag(v2f_customrendertexture IN) : SV_Target { return tex2D(_SelfTexture2D, IN.globalTexcoord.xy) + _Add; }
            ENDCG
        }
    }
}
