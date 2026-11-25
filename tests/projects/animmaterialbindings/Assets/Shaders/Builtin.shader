Shader "Q758/Builtin"
{
    Properties
    {
        _Cutoff ("Cutoff", Range(0,1)) = 0.5
        _QFloatC ("FloatC", Float) = 0
        _MainTex ("MainTex", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _QTexC ("TexC", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
}
