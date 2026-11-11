Shader "Probe/SamplerStates/Twentieths"
{
    Properties { _MainTex ("Main", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            #pragma vertex vert_img
            float4 frag(v2f_img i) : SV_Target { return round(tex2D(_MainTex, i.uv) * 20); }
            ENDCG
        }
    }
}
