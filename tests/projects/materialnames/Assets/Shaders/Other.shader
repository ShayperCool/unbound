Shader "Probe/Other"
{
    Properties
    {
        _Saved ("Saved", Float) = 9
        _Fresh ("Fresh", Float) = 8
        _FreshColor ("Fresh colour", Color) = (0, 1, 0, 1)
        _FreshTex ("Fresh texture", 2D) = "black" {}
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _FreshColor;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return _FreshColor; }
            ENDCG
        }
    }
}
