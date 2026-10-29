// The conventional names.
Shader "Probe/Plain"
{
    Properties
    {
        _MainTex("Main", 2D) = "white" {}
        _Color("Color", Color) = (1, 1, 1, 1)
        _Gloss("Gloss", Float) = 0.125
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Color; float _Gloss;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return tex2D(_MainTex, 0) * _Color * _Gloss; }
            ENDCG
        }
    }
}
