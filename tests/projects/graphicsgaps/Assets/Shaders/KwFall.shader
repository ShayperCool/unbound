Shader "Probe/KwFall"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ C_ON
            #pragma shader_feature_local B_ON
            #pragma dynamic_branch D_ON
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return 1; }
            ENDCG
        }
    }
    Fallback "Probe/KwFall2"
}
