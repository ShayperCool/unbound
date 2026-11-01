// Reads one slice of a texture array at the destination's texel centres (the probe's readback of
// array slices).
Shader "Probe/SampleSlice"
{
    Properties
    {
        _Array ("Array", 2DArray) = "" {}
        _Layer ("Layer", Float) = 0
    }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #include "UnityCG.cginc"
            UNITY_DECLARE_TEX2DARRAY(_Array);
            float _Layer;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            fixed4 frag(v2f i) : SV_Target { return UNITY_SAMPLE_TEX2DARRAY(_Array, float3(i.uv, _Layer)); }
            ENDCG
        }
    }
}
