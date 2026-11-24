Shader "Probe/UavFragWrite"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Pass 0: RWTexture2D<float>
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWTexture2D<float> _Out : register(u1);
            float _Val;
            float4 vert(float4 p : POSITION) : SV_Position { return p; }
            float4 frag(float4 p : SV_Position) : SV_Target
            {
                _Out[uint2(0, 0)] = _Val;
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        // Pass 1: RWTexture2D<float2>
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWTexture2D<float2> _Out : register(u1);
            float4 _Val4;
            float4 vert(float4 p : POSITION) : SV_Position { return p; }
            float4 frag(float4 p : SV_Position) : SV_Target
            {
                _Out[uint2(0, 0)] = _Val4.xy;
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        // Pass 2: RWTexture2D<float3>
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWTexture2D<float3> _Out : register(u1);
            float4 _Val4;
            float4 vert(float4 p : POSITION) : SV_Position { return p; }
            float4 frag(float4 p : SV_Position) : SV_Target
            {
                _Out[uint2(0, 0)] = _Val4.xyz;
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        // Pass 3: RWTexture2D<float4>
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWTexture2D<float4> _Out : register(u1);
            float4 _Val4;
            float4 vert(float4 p : POSITION) : SV_Position { return p; }
            float4 frag(float4 p : SV_Position) : SV_Target
            {
                _Out[uint2(0, 0)] = _Val4;
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        // Pass 4: RWTexture2D<uint>
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWTexture2D<uint> _Out : register(u1);
            uint _UVal;
            float4 vert(float4 p : POSITION) : SV_Position { return p; }
            float4 frag(float4 p : SV_Position) : SV_Target
            {
                _Out[uint2(0, 0)] = _UVal;
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        // Pass 5: RWTexture3D<float> (register u4, ColorMask 0, ZClip Off — VoxelizeScene pattern)
        Pass
        {
            ColorMask 0
            ZClip Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWTexture3D<float> _Out3 : register(u4);
            float _Val;
            uint _Slice;
            float4 vert(float4 p : POSITION) : SV_Position { return p; }
            float4 frag(float4 p : SV_Position) : SV_Target
            {
                _Out3[uint3(1, 2, _Slice)] = _Val;
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        // Pass 6: Explicit RWTexture2D (u1) + unnumbered RWStructuredBuffer (auto u2)
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWTexture2D<float4> _Tex : register(u1);
            RWStructuredBuffer<uint> _Buf;
            float4 _Val4;
            uint _UVal;
            float4 vert(float4 p : POSITION) : SV_Position { return p; }
            float4 frag(float4 p : SV_Position) : SV_Target
            {
                _Tex[uint2(0, 0)] = _Val4;
                _Buf[0] = _UVal;
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        // Pass 7: Read-modify-write RWTexture2D<float4>
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWTexture2D<float4> _Out : register(u1);
            float4 _Val4;
            float4 vert(float4 p : POSITION) : SV_Position { return p; }
            float4 frag(float4 p : SV_Position) : SV_Target
            {
                _Out[uint2(0, 0)] = _Out[uint2(0, 0)] + _Val4;
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        // Pass 8: switch with default falling through into case 0
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWTexture2D<float4> _Out : register(u1);
            uint _Axis;
            struct VOut { float4 pos : SV_Position; float4 val : TEXCOORD0; };
            VOut vert(float4 p : POSITION)
            {
                VOut o;
                float4 v = float4(0, 0, 0, 0);
                switch (_Axis)
                {
                    default:
                    case 0:
                        v = float4(0.125, 0.25, 0.375, 1.0);
                        break;
                    case 1:
                        v = float4(0.5, 0.625, 0.75, 1.0);
                        break;
                    case 2:
                        v = float4(0.875, 0.25, 0.5, 1.0);
                        break;
                }
                o.pos = p;
                o.val = v;
                return o;
            }
            float4 frag(VOut i) : SV_Target
            {
                _Out[uint2(0, 0)] = i.val;
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        // Pass 9: RWStructuredBuffer<uint> at u1 (for buffer<->texture slot replacement)
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            RWStructuredBuffer<uint> _Buf : register(u1);
            uint _UVal;
            float4 vert(float4 p : POSITION) : SV_Position { return p; }
            float4 frag(float4 p : SV_Position) : SV_Target
            {
                _Buf[0] = _UVal;
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
