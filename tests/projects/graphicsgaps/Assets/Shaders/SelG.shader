Shader "Probe/SelG"
{
    SubShader
    {
        Tags { "Queue"="Geometry+41" "SubId"="g0" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(170/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
        Pass
        {
            Name "ProbeStage"
            Tags { "LightMode"="ProbeStage" }
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct V { float4 pos : SV_POSITION; };
            V vert(float4 v : POSITION) { V o; o.pos = UnityObjectToClipPos(v); return o; }
            [maxvertexcount(3)]
            void geom(triangle V input[3], inout TriangleStream<V> stream)
            {
                for (int i = 0; i < 3; i++) stream.Append(input[i]);
            }
            fixed4 frag(V i) : SV_Target { return fixed4(1, 1, 0, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "Queue"="Geometry+42" "SubId"="g1" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(180/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    Fallback Off
}
