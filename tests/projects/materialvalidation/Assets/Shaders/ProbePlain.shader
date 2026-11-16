Shader "Probe/ProbePlain"
{
    Properties
    {
        _Value ("Value", Float) = 0
        _EmissionColor ("Emission", Color) = (0,0,0,1)
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _PROBE_VALIDATED
            #pragma shader_feature_local PROBE_OTHER
            #include "UnityCG.cginc"
            float _Value;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return _Value; }
            ENDCG
        }
    }
}
