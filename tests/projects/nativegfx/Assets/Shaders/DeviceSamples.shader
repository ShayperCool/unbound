Shader "Probe/NativeGfx/DeviceSamples"
{
    SubShader
    {
        Cull Off ZTest Always ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            float4 frag(uint sampleIndex : SV_SampleIndex, out float depth : SV_Depth) : SV_Target
            {
                depth = 0.125 + 0.125 * sampleIndex;
                return float4(depth, 0, 0, 1);
            }
            ENDCG
        }
    }
}
