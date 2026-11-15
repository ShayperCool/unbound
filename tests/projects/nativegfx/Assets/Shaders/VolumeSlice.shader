Shader "Probe/NativeGfx/VolumeSlice"
{
    // _Volume sampled at the output's uv and depth _W (point filtered by the texture).
    Properties { _Volume ("Volume", 3D) = "" {} _W ("Depth", Float) = 0.25 }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler3D _Volume;
            float _W;
            float4 frag(v2f_img i) : SV_Target { return tex3D(_Volume, float3(i.uv, _W)); }
            ENDCG
        }
    }
}
