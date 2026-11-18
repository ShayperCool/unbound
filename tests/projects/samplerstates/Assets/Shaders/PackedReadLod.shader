Shader "Probe/SamplerStates/PackedReadLod"
{
    // `_P` sampled through its own state at (_U, 0.5) from mip level `_Lod`.
    Properties { _MainTex ("Main", 2D) = "white" {} _U ("U", Float) = 0.25 _Lod ("Lod", Float) = 0 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _P; float _U; float _Lod;
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return tex2Dlod(_P, float4(_U, 0.5, 0, _Lod)); }
            ENDCG
        }
    }
}
