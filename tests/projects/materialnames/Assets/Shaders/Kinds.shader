Shader "Probe/Kinds"
{
    Properties
    {
        _Saved ("Saved", Color) = (0, 0, 1, 1)
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Saved;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return _Saved; }
            ENDCG
        }
    }
}
