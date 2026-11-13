Shader "Probe/TexFormatsQ58"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "UnityCG.cginc"
        Texture2D<int4> _IntFeed;
        Texture2D<uint4> _UIntFeed;
        Texture2D<float> _DepthTex;
        struct v2f { float4 pos : SV_POSITION; };
        v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
        int4 feedInt(v2f i) : SV_Target { return _IntFeed.Load(int3((int)i.pos.x, 0, 0)).yzwx; }
        uint4 feedUInt(v2f i) : SV_Target { return _UIntFeed.Load(int3((int)i.pos.x, 0, 0)).yzwx; }
        float4 readDepth(v2f i) : SV_Target
        {
            float d = round(_DepthTex.Load(int3((int)i.pos.x, 0, 0)) * 16) / 16;
            return float4(d, d, d, 1);
        }
        ENDHLSL
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment feedInt
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment feedUInt
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment readDepth
            ENDHLSL }
    }
}
