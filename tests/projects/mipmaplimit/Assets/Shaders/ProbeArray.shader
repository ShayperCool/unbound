Shader "Probe/Array"
{
    Properties { _Tex ("Texture array", 2DArray) = "" {} }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #include "UnityCG.cginc"
            UNITY_DECLARE_TEX2DARRAY(_Tex);
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            fixed4 frag(v2f i) : SV_Target { return UNITY_SAMPLE_TEX2DARRAY(_Tex, float3(i.uv, 1)); }
            ENDCG
        }
    }
}
