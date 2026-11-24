Shader "Probe/SelX"
{
    SubShader
    {
        Tags { "Queue"="Geometry+11" "SubId"="x0" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma exclude_renderers glcore
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(110/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "Queue"="Geometry+12" "SubId"="x1" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(120/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    Fallback Off
}
