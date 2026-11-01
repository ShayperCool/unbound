// Update shader reading another texture's center (_Other) plus _Add.
Shader "CustomRT/Depend"
{
    Properties { _Other ("Other", 2D) = "black" {} _Add ("Add", Vector) = (0, 0, 0, 0) }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #include "UnityCustomRenderTexture.cginc"
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            sampler2D _Other;
            float4 _Add;
            float4 frag(v2f_customrendertexture IN) : SV_Target { return tex2D(_Other, float2(0.5, 0.5)) + _Add; }
            ENDCG
        }
    }
}
