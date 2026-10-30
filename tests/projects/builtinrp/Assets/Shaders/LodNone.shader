// A SubShader without a LOD: yellow.
Shader "Probe/LodNone"
{
    SubShader
    {
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(1, 1, 0, 1); }
            ENDCG }
    }
}
