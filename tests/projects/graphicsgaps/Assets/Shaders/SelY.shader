Shader "Probe/SelY"
{
    SubShader
    {
        Tags { "Queue"="Geometry+21" "SubId"="y0" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(130/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
        Pass
        {
            Name "ProbeExtra"
            Tags { "LightMode"="ProbeExtra" }
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma exclude_renderers glcore
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(130/255.0, 40/255.0, 0, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "Queue"="Geometry+22" "SubId"="y1" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(140/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    Fallback Off
}
