// Writes the light variables a ForwardBase pass sees, one vector per pixel along x, over the target.
Shader "Probe/ObjectGlobals"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return float4(v.xy * 2.0, 0.5, 1.0); }
            float4 frag(float4 pos : SV_POSITION) : SV_Target
            {
                int i = (int)pos.x;
                if (i == 0) return _WorldSpaceLightPos0;
                if (i == 1) return _LightColor0;
                return float4(-1, -1, -1, -1);
            }
            ENDCG
        }
    }
}
