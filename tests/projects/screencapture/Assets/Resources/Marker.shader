// A constant color, unconverted (a vector property): the end-of-frame marker.
Shader "Probe/Marker"
{
    Properties { _Value ("Value", Vector) = (1, 0, 1, 0.8) }
    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Value;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return _Value; }
            ENDCG
        }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Value;
            struct targets { float4 first : SV_Target0; float4 second : SV_Target1; };
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            targets frag()
            {
                targets t;
                t.first = _Value;
                t.second = _Value.bgra;
                return t;
            }
            ENDCG
        }
    }
}
