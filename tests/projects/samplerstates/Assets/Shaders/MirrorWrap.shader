Shader "Probe/SamplerStates/MirrorWrap"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture3D _Vol;
            SamplerState sampler_Vol;
            SamplerState sampler_PointMirrorOnce;
            float3 _Axis;
            float _Inline;
            float sampleAxis(float u)
            {
                float3 uv = .25 + _Axis * (u - .25);
                return _Inline > .5 ? _Vol.Sample(sampler_PointMirrorOnce, uv).r : _Vol.Sample(sampler_Vol, uv).r;
            }
            float4 frag(v2f_img i) : SV_Target
            {
                return float4(sampleAxis(.4), sampleAxis(-.4), sampleAxis(1.6), sampleAxis(-1.6));
            }
            ENDHLSL
        }
    }
}
