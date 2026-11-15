// Every texel of one mip of a cubemap, one strip of its faces (+X, -X, +Y, -Y, +Z, -Z; the first
// _Faces of them) a face wide each: the texel at (x, y) of a face is sampled through the
// direction to its centre.
Shader "Hidden/CubeTexels"
{
    Properties
    {
        _Cube ("Cube", Cube) = "" {}
        _Mip ("Mip", Float) = 0
        _Size ("Face size of the mip", Float) = 8
        _Faces ("Faces drawn, from +X", Float) = 6
    }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _Cube;
            float _Mip;
            float _Size;
            float _Faces;

            float4 frag(v2f_img i) : SV_Target
            {
                float column = floor(i.uv.x * _Faces * _Size);
                float face = floor(column / _Size);
                float x = column - face * _Size;
                float y = floor(i.uv.y * _Size);
                float u = (x + 0.5) / _Size * 2 - 1;
                float v = (y + 0.5) / _Size * 2 - 1;
                float3 d = face < 0.5 ? float3(1, -v, -u)
                    : face < 1.5 ? float3(-1, -v, u)
                    : face < 2.5 ? float3(u, 1, v)
                    : face < 3.5 ? float3(u, -1, -v)
                    : face < 4.5 ? float3(u, -v, 1)
                    : float3(-u, -v, -1);
                return texCUBElod(_Cube, float4(d, _Mip));
            }
            ENDCG
        }
    }
}
