Shader "Probe/Volume"
{
    Properties { _Volume ("Volume", 3D) = "" {} _Depth ("Depth", Float) = 0 }
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
            float _Depth;
            fixed4 frag(v2f_img i) : SV_Target { return tex3D(_Volume, float3(i.uv, _Depth)); }
            ENDCG
        }
    }
}
