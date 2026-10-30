// One colour per SubShader LOD: 300 red, 200 green, 100 blue; below 100 the fallback.
Shader "Probe/LodColors"
{
    CGINCLUDE
    #include "UnityCG.cginc"
    float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
    ENDCG
    SubShader
    {
        LOD 300
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag() : SV_Target { return float4(1, 0, 0, 1); }
            ENDCG }
    }
    SubShader
    {
        LOD 200
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag() : SV_Target { return float4(0, 1, 0, 1); }
            ENDCG }
    }
    SubShader
    {
        LOD 100
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag() : SV_Target { return float4(0, 0, 1, 1); }
            ENDCG }
    }
    Fallback "Probe/LodNone"
}
