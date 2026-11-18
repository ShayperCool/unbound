// A shader without a custom material editor, so that material rows (mat.*) show serialized
// object behaviour alone (a ShaderGUI's ValidateMaterial would also change the material).
Shader "Probe/Plain"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature _EMISSION
            #pragma shader_feature _ALPHATEST_ON
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
