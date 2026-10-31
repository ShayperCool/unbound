Shader "Probe/MipLevels"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} _Level ("Mip level", Float) = 0 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Level;
            float4 frag(v2f_img i) : SV_Target { return tex2Dlod(_MainTex, float4(i.uv, 0, _Level)); }
            ENDCG
        }
    }
}
