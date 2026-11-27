Shader "Probe/Locked"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} _Other ("Other", 2D) = "black" {} _Locked ("Locked", 2D) = "gray" {} _Tint ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Other, _Locked;
            fixed4 _Tint;
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return tex2D(_MainTex, 0) * tex2D(_Other, 0) * tex2D(_Locked, 0) * _Tint; }
            ENDCG
        }
    }
}
