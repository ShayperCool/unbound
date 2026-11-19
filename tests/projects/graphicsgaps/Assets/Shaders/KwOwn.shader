Shader "Probe/KwOwn"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ A_ON
            #pragma shader_feature B_ON
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return 1; }
            ENDCG
        }
    }
    Fallback "Probe/KwFall"
}
