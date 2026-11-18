Shader "Probe/SamplerStates/PackedFill"
{
    // Packed target fills: _Mode 0 writes (0.25, 0.5, 0.3, 0.7) everywhere, 1 writes 0.2 into the
    // left half and 0.6 into the right half (every channel), 2 adds 0.04 to every channel
    // (Blend One One).
    Properties { _MainTex ("Main", 2D) = "white" {} _Mode ("Mode", Float) = 0 _Src ("Src", Float) = 1 _Dst ("Dst", Float) = 0 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Blend [_Src] [_Dst]
            CGPROGRAM
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            float _Mode;
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target
            {
                if (_Mode < 0.5) return float4(0.25, 0.5, 0.3, 0.7);
                if (_Mode < 1.5) return i.uv.x < 0.5 ? 0.2 : 0.6;
                return 0.04;
            }
            ENDCG
        }
    }
}
