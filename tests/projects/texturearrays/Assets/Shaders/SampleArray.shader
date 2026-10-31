// Draws one slice of a Texture2DArray at one mip level with the texture's own sampler.
Shader "Probe/SampleArray"
{
    Properties
    {
        _Arr ("Array", 2DArray) = "" {}
        _Slice ("Slice", Float) = 0
        _Lod ("Mip level", Float) = 0
    }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma require 2darray
            #include "UnityCG.cginc"
            UNITY_DECLARE_TEX2DARRAY(_Arr);
            float _Slice;
            float _Lod;
            float4 frag(v2f_img i) : SV_Target
            {
                return UNITY_SAMPLE_TEX2DARRAY_LOD(_Arr, float3(i.uv, _Slice), _Lod);
            }
            ENDCG
        }
    }
}
