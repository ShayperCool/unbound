Shader "Probe/SelQ11"
{
    SubShader
    {
        Tags { "Queue"="GEOMETRY" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(210/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    Fallback Off
}
