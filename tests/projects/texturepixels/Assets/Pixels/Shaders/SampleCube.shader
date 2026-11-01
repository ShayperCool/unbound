// Draws one face of a cubemap at one mip level with the texture's own sampler: the target's
// texel (u, v) samples the face at s = 2u - 1, t = 1 - 2v (t runs down the face).
Shader "Probe/SampleCube"
{
    Properties
    {
        _Cube ("Cube", Cube) = "" {}
        _Face ("Face", Float) = 0
        _Lod ("Mip level", Float) = 0
    }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            samplerCUBE _Cube;
            float _Face;
            float _Lod;
            float4 frag(v2f_img i) : SV_Target
            {
                float s = i.uv.x * 2 - 1, t = 1 - i.uv.y * 2;
                float3 d;
                if (_Face < 0.5) d = float3(1, -t, -s);
                else if (_Face < 1.5) d = float3(-1, -t, s);
                else if (_Face < 2.5) d = float3(s, 1, t);
                else if (_Face < 3.5) d = float3(s, -1, -t);
                else if (_Face < 4.5) d = float3(s, -t, 1);
                else d = float3(-s, -t, -1);
                return texCUBElod(_Cube, float4(d, _Lod));
            }
            ENDCG
        }
    }
}
