// What a ForwardBase draw gets for its main light: unity_OcclusionMaskSelector, then
// _LightColor0, into a 2x1 float target. The quad covers the target whatever the matrices are.
Shader "Hidden/SelectorDump"
{
    SubShader
    {
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off ZTest Always ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return float4(vertex.xy, 0.5, 1); }
            float4 frag(float4 position : SV_POSITION) : SV_Target
            {
                return (int)position.x == 0 ? unity_OcclusionMaskSelector : _LightColor0;
            }
            ENDCG
        }
    }
}
