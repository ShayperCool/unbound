Shader "Probe/SamplerStates/UsedVertex"
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
            TextureCube _Cube; Texture2D _B; SamplerState sampler_B;
            static const float2 P = float2(0.4, 0.5);
            static const float3 D = float3(1, 0.2, 0);
            #pragma vertex vert
            struct V { float4 pos : SV_POSITION; float4 loaded : TEXCOORD0; };
            V vert(appdata_img v) { V o; o.pos = UnityObjectToClipPos(v.vertex); o.loaded = _B.Load(int3(1, 0, 0)); return o; }
            float4 frag(V i) : SV_Target { return float4(_Cube.Sample(sampler_B, D).r, i.loaded.r, 0, 1); }
            ENDCG
        }
    }
}
