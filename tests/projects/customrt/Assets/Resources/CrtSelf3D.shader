// Update shader for volumes: the previous content (_SelfTexture3D) plus _Add.
Shader "CustomRT/Self3D"
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
            float4 frag(v2f_customrendertexture IN) : SV_Target { return tex3D(_SelfTexture3D, IN.globalTexcoord) + _Add; }
            ENDCG
        }
    }
}
