// One texel of the texture _Pick selects, as sampled: the properties' defaults (white, black,
// gray, grey, bump, red, linearGray), then a sampler without a property. Alpha is written too.
Shader "Probe/DefaultTexels"
{
    Properties
    {
        _T0 ("white", 2D) = "white" {}
        _T1 ("black", 2D) = "black" {}
        _T2 ("gray", 2D) = "gray" {}
        _T3 ("grey", 2D) = "grey" {}
        _T4 ("bump", 2D) = "bump" {}
        _T5 ("red", 2D) = "red" {}
        _T6 ("linearGray", 2D) = "linearGray" {}
        _Pick ("Pick", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _T0, _T1, _T2, _T3, _T4, _T5, _T6, _Unassigned;
            float _Pick;
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            float4 frag(v2f i) : SV_Target
            {
                float2 uv = float2(0.5, 0.5);
                int c = (int)(_Pick + 0.5);
                if (c == 0) return tex2D(_T0, uv);
                if (c == 1) return tex2D(_T1, uv);
                if (c == 2) return tex2D(_T2, uv);
                if (c == 3) return tex2D(_T3, uv);
                if (c == 4) return tex2D(_T4, uv);
                if (c == 5) return tex2D(_T5, uv);
                if (c == 6) return tex2D(_T6, uv);
                return tex2D(_Unassigned, uv);
            }
            ENDCG
        }
    }
}
