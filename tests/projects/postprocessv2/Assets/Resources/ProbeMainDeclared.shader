// A fullscreen triangle sampling _MainTex at its centre; the Properties block declares it.
Shader "Hidden/PostProcessProbe/MainDeclared"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            Texture2D _MainTex;
            SamplerState sampler_MainTex;
            float4 vert(float3 position : POSITION) : SV_POSITION { return float4(position.xy, 0, 1); }
            float4 frag() : SV_Target { return _MainTex.Sample(sampler_MainTex, float2(0.5, 0.5)); }
            ENDHLSL
        }
    }
}
