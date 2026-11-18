Shader "Probe/SamplerStates/DepthUint"
{
    // A depth target read through an integer slot: the first texel's four channels as unsigned
    // words, each output as one 16-bit half (_High 1: the high halves, 0: the low halves).
    Properties { _MainTex ("Main", 2D) = "white" {} _High ("High", Float) = 1 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            Texture2D<uint4> _Shadow;
            float _High;
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target
            {
                uint4 v = _Shadow.Load(int3(0, 0, 0));
                return _High > 0.5 ? float4(v >> 16) : float4(v & 0xFFFF);
            }
            ENDCG
        }
    }
}
