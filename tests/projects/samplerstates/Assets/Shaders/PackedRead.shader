Shader "Probe/SamplerStates/PackedRead"
{
    // `_P` sampled through its own state at (_U, 0.5).
    Properties { _MainTex ("Main", 2D) = "white" {} _U ("U", Float) = 0.25 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _P; float _U;
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return tex2D(_P, float2(_U, 0.5)); }
            ENDCG
        }
    }
}
