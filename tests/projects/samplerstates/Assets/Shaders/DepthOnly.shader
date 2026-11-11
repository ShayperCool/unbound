Shader "Probe/SamplerStates/DepthOnly"
{
    Properties { _MainTex ("Main", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite On ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #pragma vertex vert_img
            float frag(v2f_img i) : SV_Depth { return i.uv.x < 0.5 ? 0.25 : 0.75; }
            ENDCG
        }
    }
}
