Shader "Probe/ClipSolid"
{
    SubShader
    {
        Cull Off ZTest Always ZWrite Off
        HLSLINCLUDE
        float4 vertex(float4 p: POSITION): SV_POSITION { return p; }
        float4 white(): SV_Target { return float4(1,1,1,1); }
        struct Outputs { float4 a: SV_Target0; float4 b: SV_Target1; float4 c: SV_Target2; };
        Outputs multiple() { Outputs o; o.a = float4(0,0,1,1); o.b = float4(1,0,0,1); o.c = float4(0,1,0,1); return o; }
        ENDHLSL
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vertex
            #pragma fragment white
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vertex
            #pragma fragment multiple
            ENDHLSL
        }
    }
}
