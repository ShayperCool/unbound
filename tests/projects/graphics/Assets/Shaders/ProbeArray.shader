Shader "Probe/Array"
{
    Properties { _Array ("Array", 2DArray) = "" {} _Slice ("Slice", Float) = 0 }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma require 2darray
            #include "UnityCG.cginc"
            UNITY_DECLARE_TEX2DARRAY(_Array);
            float _Slice;
            fixed4 frag(v2f_img i) : SV_Target { return UNITY_SAMPLE_TEX2DARRAY(_Array, float3(i.uv, _Slice)); }
            ENDCG
        }
    }
}
