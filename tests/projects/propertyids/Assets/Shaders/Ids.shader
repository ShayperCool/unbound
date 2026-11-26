Shader "Probe/Ids"
{
    Properties
    {
        _ProbeOwnFirst ("Own first", Float) = 1
        _MainTex ("Texture", 2D) = "white" {}
        _ProbeOwnSecond ("Own second", Color) = (1,1,1,1)
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _ProbeOwnFirst;
            float4 _ProbeOwnSecond;
            sampler2D _MainTex;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return tex2D(_MainTex, float2(0, 0)) * _ProbeOwnSecond * _ProbeOwnFirst; }
            ENDCG
        }
    }
}
