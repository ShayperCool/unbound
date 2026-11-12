// The probe's program: red is SHADER_TARGET, green the SHADER_AVAILABLE_<feature> defines (one
// bit each, in this order), blue the probe keywords the variant has (one bit each, `keywords`),
// alpha SHADER_TARGET_AVAILABLE (-1 when it is not defined).
// No includes: the built-in keywords (INSTANCING_ON) would change what an include declares.
// Graphics.Blit draws its quad from 0 to 1, which covers the one texel either way up.
struct v2f { float4 pos : SV_POSITION; };
v2f vert(float4 v : POSITION) { v2f o; o.pos = float4(v.xy * 2 - 1, 0.5, 1); return o; }
float features()
{
    float f = 0;
#ifdef SHADER_AVAILABLE_DERIVATIVES
    f += 1;
#endif
#ifdef SHADER_AVAILABLE_INTERPOLATORS10
    f += 2;
#endif
#ifdef SHADER_AVAILABLE_SAMPLELOD
    f += 4;
#endif
#ifdef SHADER_AVAILABLE_FRAGCOORD
    f += 8;
#endif
#ifdef SHADER_AVAILABLE_INTERPOLATORS15
    f += 16;
#endif
#ifdef SHADER_AVAILABLE_MRT4
    f += 32;
#endif
#ifdef SHADER_AVAILABLE_INTEGERS
    f += 64;
#endif
#ifdef SHADER_AVAILABLE_2DARRAY
    f += 128;
#endif
#ifdef SHADER_AVAILABLE_INSTANCING
    f += 256;
#endif
#ifdef SHADER_AVAILABLE_COMPUTE
    f += 512;
#endif
#ifdef SHADER_AVAILABLE_RANDOMWRITE
    f += 1024;
#endif
#ifdef SHADER_AVAILABLE_MSAATEX
    f += 2048;
#endif
#ifdef SHADER_AVAILABLE_CUBEARRAY
    f += 4096;
#endif
#ifdef SHADER_AVAILABLE_TESSHW
    f += 8192;
#endif
#ifdef SHADER_AVAILABLE_GEOMETRY
    f += 16384;
#endif
#ifdef SHADER_AVAILABLE_TESSELLATION
    f += 32768;
#endif
#ifdef SHADER_AVAILABLE_MRT8
    f += 65536;
#endif
#ifdef SHADER_AVAILABLE_UAV
    f += 131072;
#endif
#ifdef SHADER_AVAILABLE_INTERPOLATORS32
    f += 262144;
#endif
#ifdef SHADER_AVAILABLE_SPARSETEX
    f += 524288;
#endif
#ifdef SHADER_AVAILABLE_FRAMEBUFFERFETCH
    f += 1048576;
#endif
#ifdef SHADER_AVAILABLE_FBFETCH
    f += 2097152;
#endif
#ifdef SHADER_AVAILABLE_SETRTARRAYINDEXFROMANYSHADER
    f += 4194304;
#endif
    return f;
}
float keywords()
{
    float k = 0;
#ifdef KW_A
    k += 1;
#endif
#ifdef KW_B
    k += 2;
#endif
#ifdef INSTANCING_ON
    k += 4;
#endif
#ifdef SHADOWS_SCREEN
    k += 8;
#endif
#ifdef PROCEDURAL_INSTANCING_ON
    k += 16;
#endif
#ifdef DYNAMICLIGHTMAP_ON
    k += 32;
#endif
#ifdef SHADOWS_SOFT
    k += 64;
#endif
#ifdef DIRLIGHTMAP_COMBINED
    k += 128;
#endif
    return k;
}
float4 frag(v2f i) : SV_Target
{
#ifdef SHADER_TARGET_AVAILABLE
    float available = SHADER_TARGET_AVAILABLE;
#else
    float available = -1;
#endif
    return float4(SHADER_TARGET, features(), keywords(), available);
}
