Shader "Probe/SurfaceIndices/Plain"
{
    SubShader
    {
        CGPROGRAM
        #pragma surface surf Lambert 
        struct Input { float2 uv_MainTex; };
        void surf(Input i, inout SurfaceOutput o) { o.Albedo = 0; o.Emission = float3(0.1,0.2,0.3); o.Alpha = 1; }
        ENDCG
        Pass
        {
            Name "TAIL"
            Tags { "LightMode" = "Always" "PassId" = "tail" }
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 p : POSITION) : SV_POSITION { return float4(p.xy * 2 - 1, 0.5, 1); }
            float4 frag() : SV_TARGET { return float4(0.25,0.5,0.75,1); }
            ENDHLSL
        }
    }
    Fallback Off
}
