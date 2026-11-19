Shader "Probe/SelF"
{
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "Queue"="Geometry+600" "SubId"="f0" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(150/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderPipeline"="Foo" "Queue"="Geometry+700" "SubId"="f1" }
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
        Pass
        {
            Tags { "LightMode"="ProbeExtra" }
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(160/255.0, 32/255.0, 0, 1); }
            ENDCG
        }
    }
    Fallback "Probe/SelFF"
}
