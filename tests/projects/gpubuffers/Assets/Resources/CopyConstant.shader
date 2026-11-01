Shader "Hidden/GpuBuffers/CopyConstant"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            cbuffer CopyValues { float4 _CopiedColor; };
            float4 frag(v2f_img i) : SV_Target { return _CopiedColor; }
            ENDCG
        }
    }
}
