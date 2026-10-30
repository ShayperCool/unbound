Shader "Probe/FloatShapes"
{
    SubShader
    {
        Cull Off ZTest Always ZWrite Off
        HLSLINCLUDE
        #include "UnityCG.cginc"
        UNITY_DECLARE_TEX2DARRAY(_Array);
        sampler3D _Volume;
        samplerCUBE _Cube;
        UNITY_DECLARE_TEXCUBEARRAY(_Cubes);
        float4 vertex(float4 p: POSITION): SV_POSITION { return UnityObjectToClipPos(p); }
        float4 arraySample(): SV_Target { return UNITY_SAMPLE_TEX2DARRAY_LOD(_Array, float3(0.5,0.5,0), 0); }
        float4 volumeSample(): SV_Target { return tex3Dlod(_Volume, float4(0.5,0.5,0.5,0)); }
        float4 cubeSample(): SV_Target { return texCUBElod(_Cube, float4(1,0,0,0)); }
        float4 cubesSample(): SV_Target { return UNITY_SAMPLE_TEXCUBEARRAY_LOD(_Cubes, float4(1,0,0,0), 0); }
        ENDHLSL
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertex
            #pragma fragment arraySample
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertex
            #pragma fragment volumeSample
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertex
            #pragma fragment cubeSample
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertex
            #pragma fragment cubesSample
            ENDHLSL
        }
    }
}
