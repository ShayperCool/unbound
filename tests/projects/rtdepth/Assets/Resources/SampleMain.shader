// Returns what a filtered float sample of _MainTex gives, all four channels, unchanged.
Shader "Probe/SampleMain"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 frag(v2f_img i) : SV_Target { return tex2D(_MainTex, i.uv); }
            ENDCG
        }
    }
}
