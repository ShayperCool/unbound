Shader "Probe/SelO"
{
    SubShader
    {
        Tags { "Queue"="Geometry+31" "SubId"="o0" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma only_renderers vulkan
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(150/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "Queue"="Geometry+32" "SubId"="o1" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(160/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    Fallback Off
}
