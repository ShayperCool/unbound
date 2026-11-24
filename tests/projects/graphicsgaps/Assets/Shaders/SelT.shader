Shader "Probe/SelT"
{
    SubShader
    {
        Tags { "Queue"="Geometry+51" "SubId"="t0" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(190/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
        Pass
        {
            Name "ProbeStage"
            Tags { "LightMode"="ProbeStage" }
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma target 4.6
            #pragma vertex vert
            #pragma hull hull
            #pragma domain domain
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct C { float4 vertex : INTERNALTESSPOS; };
            struct F { float edge[3] : SV_TessFactor; float inside : SV_InsideTessFactor; };
            struct V { float4 pos : SV_POSITION; };
            C vert(float4 v : POSITION) { C o; o.vertex = v; return o; }
            F patch(InputPatch<C, 3> p) { F f; f.edge[0] = f.edge[1] = f.edge[2] = 1; f.inside = 1; return f; }
            [domain("tri")]
            [partitioning("integer")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("patch")]
            [outputcontrolpoints(3)]
            C hull(InputPatch<C, 3> p, uint id : SV_OutputControlPointID) { return p[id]; }
            [domain("tri")]
            V domain(F f, OutputPatch<C, 3> p, float3 b : SV_DomainLocation)
            {
                V o;
                o.pos = UnityObjectToClipPos(p[0].vertex * b.x + p[1].vertex * b.y + p[2].vertex * b.z);
                return o;
            }
            fixed4 frag(V i) : SV_Target { return fixed4(1, 0, 1, 1); }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "Queue"="Geometry+52" "SubId"="t1" }
        Pass
        {
            
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return fixed4(200/255.0, 0/255.0, 0, 1); }
            ENDCG
        }
    }
    Fallback Off
}
