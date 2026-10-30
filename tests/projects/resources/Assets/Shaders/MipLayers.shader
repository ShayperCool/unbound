Shader "Probe/MipLayers"
{
    Properties
    {
        _Cube ("Cube", Cube) = "black" {}
        _Array ("Array", 2DArray) = "" {}
        _Level ("Level", Float) = 0
        _Layer ("Layer", Float) = 0
        _Direction ("Direction", Vector) = (1,0,0,0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        samplerCUBE _Cube;
        UNITY_DECLARE_TEX2DARRAY(_Array);
        float _Level, _Layer;
        float4 _Direction;
        float4 cube(v2f_img i) : SV_Target { return texCUBElod(_Cube, float4(_Direction.xyz, _Level)); }
        float4 array(v2f_img i) : SV_Target { return UNITY_SAMPLE_TEX2DARRAY_LOD(_Array, float3(i.uv, _Layer), _Level); }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment cube
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment array
            ENDCG
        }
    }
}
