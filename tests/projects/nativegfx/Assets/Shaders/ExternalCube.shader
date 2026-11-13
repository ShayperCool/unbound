Shader "Probe/NativeGfx/ExternalCube"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            samplerCUBE _Cube;
            float _Level;
            float4 frag(v2f_img i) : SV_Target { return texCUBElod(_Cube, float4(0, -1, 0, _Level)); }
            ENDCG
        }
    }
}
