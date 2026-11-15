Shader "Probe/LineShadowCubeRead"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(float4 p : POSITION) { v2f o; o.pos=float4(p.xy*2-1,0.5,1);o.uv=p.xy*2-1;return o; }
            TextureCube<float> _ProbeShadowCube;
            SamplerComparisonState sampler_ProbeShadowCube;
            float _ProbeCubeFace;
            float4 frag(v2f i) : SV_Target
            {
                float2 p=i.uv;
                float3 d = _ProbeCubeFace < 0.5 ? float3(1,-p.y,-p.x) :
                    _ProbeCubeFace < 1.5 ? float3(-1,-p.y,p.x) :
                    _ProbeCubeFace < 2.5 ? float3(p.x,1,p.y) :
                    _ProbeCubeFace < 3.5 ? float3(p.x,-1,-p.y) :
                    _ProbeCubeFace < 4.5 ? float3(p.x,-p.y,1) : float3(-p.x,-p.y,-1);
                // Three depth comparisons retain coverage and width-bias threshold changes.
                float3 v=float3(_ProbeShadowCube.SampleCmpLevelZero(sampler_ProbeShadowCube,d,0.51),
                    _ProbeShadowCube.SampleCmpLevelZero(sampler_ProbeShadowCube,d,0.53),
                    _ProbeShadowCube.SampleCmpLevelZero(sampler_ProbeShadowCube,d,0.55));
                return float4(v*0.5+0.5,1);
            }
            ENDCG
        }
    }
}
