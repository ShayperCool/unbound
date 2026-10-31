// Draws one depth of a Texture3D at one mip level with the texture's own sampler.
Shader "Probe/SampleVolume"
{
    Properties
    {
        _Vol ("Volume", 3D) = "" {}
        _Z ("Depth", Float) = 0.5
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
            #include "UnityCG.cginc"
            sampler3D _Vol;
            float _Z;
            float _Lod;
            float4 frag(v2f_img i) : SV_Target
            {
                return tex3Dlod(_Vol, float4(i.uv, _Z, _Lod));
            }
            ENDCG
        }
    }
}
