// Returns _Src_TexelSize (the sample keeps _Src bound; a depth is never above 4).
Shader "Probe/TexelSize"
{
    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Src;
            float4 _Src_TexelSize;
            float4 frag(v2f_img i) : SV_Target { return _Src_TexelSize + (tex2D(_Src, i.uv).x > 4 ? 1 : 0); }
            ENDCG
        }
    }
}
