Shader "Probe/SamplerStates/Vertex"
{
    Properties { _MainTex ("Main", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            Texture2D _A; SamplerState sampler_A; Texture2D _B; SamplerState sampler_B;
            static const float2 P = float2(0.4, 0.5);
            static const float3 D = float3(1, 0.2, 0);
            #pragma vertex vert
            struct V { float4 pos : SV_POSITION; float4 c : COLOR; };
            V vert(appdata_img v) { V o; o.pos = UnityObjectToClipPos(v.vertex); o.c = float4(_A.SampleLevel(sampler_B, P, 0).r, 0, 0, 0); return o; }
            float4 frag(V i) : SV_Target { return float4(i.c.r, _A.Sample(sampler_A, P).r, _A.Sample(sampler_B, P).r, _B.Sample(sampler_B, P).r); }
            ENDCG
        }
    }
}
