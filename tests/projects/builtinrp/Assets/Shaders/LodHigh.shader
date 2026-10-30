// Only a LOD 400 SubShader (magenta) and no fallback: what draws when it is out of reach.
Shader "Probe/LodHigh"
{
    SubShader
    {
        LOD 400
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(1, 0, 1, 1); }
            ENDCG }
    }
    Fallback Off
}
