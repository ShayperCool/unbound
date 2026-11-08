Shader "Probe/VolumeTargets"
{
    Properties { _Volume ("Volume", 3D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        float4 _Color;
        sampler3D _Volume;
        float _Slice;
        float4 solid(v2f_img i) : SV_Target { return _Color; }
        float4 sampleVolume(v2f_img i) : SV_Target { return tex3D(_Volume, float3(i.uv, _Slice)); }
        float4 spatial(v2f_img i) : SV_Target { return float4(i.uv, _Slice, 1); }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment solid
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment sampleVolume
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment spatial
            ENDCG
        }
    }
}
