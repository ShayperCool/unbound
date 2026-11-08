Shader "Hidden/AssetCloneCube"
{
    Properties { _Cube ("Cube", Cube) = "" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            samplerCUBE _Cube;
            float4 _Direction;
            float _Mip;
            float4 frag(v2f_img i) : SV_Target { return texCUBElod(_Cube, float4(_Direction.xyz, _Mip)); }
            ENDCG
        }
    }
}
