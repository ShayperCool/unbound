// Unlit opaque without a RenderType tag (left out of the depth-normals texture) and without a
// shadow caster pass (left out of the depth texture).
Shader "Probe/NoRenderType"
{
    Properties { _Color ("Color", Color) = (1, 0.5, 0, 1) }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
