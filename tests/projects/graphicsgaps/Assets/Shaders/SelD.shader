Shader "Probe/SelD"
{
    SubShader
    {
        Tags { "RenderPipeline"=" UniversalPipeline" "Queue"="Transparent+100" "SubId"="d0" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(80/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderPipeline"="universalpipeline" "Queue"="Transparent+200" "SubId"="d1" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(90/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="ProbeExtra" }
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(90/255.0, 32/255.0, 0, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderPipeline"="" "Queue"="Transparent+300" "SubId"="d2" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(100/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="ProbeExtra" }
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(100/255.0, 32/255.0, 0, 1); }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="ProbeExtra" }
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(100/255.0, 64/255.0, 0, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "Queue"="Transparent+400" "SubId"="d3" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(110/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    Fallback Off
}
