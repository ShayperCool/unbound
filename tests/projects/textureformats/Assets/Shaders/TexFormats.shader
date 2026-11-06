Shader "Probe/TexFormats"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "UnityCG.cginc"
        float4 _Color, _Color2;
        sampler2D _MainTex;
        Texture2D<int4> _IntTex;
        Texture2D<uint4> _UIntTex;
        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
        // A 2x1 target: texel 0 gets A, texel 1 gets B.
        #define PICK(pos, a, b) ((int)(pos).x == 0 ? (a) : (b))
        float4 solid(v2f i) : SV_Target { return _Color; }
        float4 split(v2f i) : SV_Target { return PICK(i.pos, _Color, _Color2); }
        float4 sample(v2f i) : SV_Target { return tex2Dlod(_MainTex, float4(i.uv, 0, 0)); }
        float4 loadInt(v2f i) : SV_Target { return asfloat(_IntTex.Load(int3((int)i.pos.x, 0, 0))); }
        float4 loadUInt(v2f i) : SV_Target { return asfloat(_UIntTex.Load(int3((int)i.pos.x, 0, 0))); }
        int4 int8(v2f i) : SV_Target { return PICK(i.pos, int4(-128, 127, -1, 5), int4(1, -2, 3, -4)); }
        uint4 uint8(v2f i) : SV_Target { return PICK(i.pos, uint4(255, 0, 128, 7), uint4(1, 2, 3, 4)); }
        int4 int16(v2f i) : SV_Target { return PICK(i.pos, int4(-32768, 32767, -2, 300), int4(1, -2, 3, -4)); }
        uint4 uint16(v2f i) : SV_Target { return PICK(i.pos, uint4(65535, 0, 32768, 1234), uint4(1, 2, 3, 4)); }
        int4 int32(v2f i) : SV_Target { return PICK(i.pos, int4(asint(0x80000000u), 2147483647, -3, 70000), int4(1, -2, 3, -4)); }
        uint4 uint32(v2f i) : SV_Target { return PICK(i.pos, uint4(0xFFFFFFFFu, 0, 0x80000000u, 123456), uint4(1, 2, 3, 4)); }
        ENDHLSL
        // 0: solid _Color
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment solid
            ENDHLSL }
        // 1: _Color blended over the target (straight alpha)
        Pass { Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment solid
            ENDHLSL }
        // 2: _Color in texel 0, _Color2 in texel 1
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment split
            ENDHLSL }
        // 3: _MainTex at the pixel's uv
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment sample
            ENDHLSL }
        // 4, 5: the bits of the signed / unsigned integer texel
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment loadInt
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment loadUInt
            ENDHLSL }
        // 6..11: integer outputs, in range for 8, 16 and 32 bits, signed and unsigned
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment int8
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment uint8
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment int16
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment uint16
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment int32
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment uint32
            ENDHLSL }
    }
}
