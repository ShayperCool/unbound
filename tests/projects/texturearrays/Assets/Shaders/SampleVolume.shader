// Draws one depth of a Texture3D at mip 0 with the texture's own sampler.
Shader "Probe/SampleVolume"
{
    Properties
    {
        _Vol ("Volume", 3D) = "" {}
        _Z ("Depth", Float) = 0.5
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
            float4 frag(v2f_img i) : SV_Target
            {
                return tex3Dlod(_Vol, float4(i.uv, _Z, 0));
            }
            ENDCG
        }
    }
}
