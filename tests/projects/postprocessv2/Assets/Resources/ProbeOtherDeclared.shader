// A fullscreen triangle sampling _OtherTex at its centre; the Properties block declares it.
Shader "Hidden/PostProcessProbe/OtherDeclared"
{
    Properties { _OtherTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            Texture2D _OtherTex;
            SamplerState sampler_OtherTex;
            float4 vert(float3 position : POSITION) : SV_POSITION { return float4(position.xy, 0, 1); }
            float4 frag() : SV_Target { return _OtherTex.Sample(sampler_OtherTex, float2(0.5, 0.5)); }
            ENDHLSL
        }
    }
}
