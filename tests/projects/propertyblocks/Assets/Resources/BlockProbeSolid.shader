// The colour the draw was given: _Color, unlit (meshes and the terrain draw with it).
Shader "Hidden/BlockProbeSolid"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Other ("Other", Float) = 0
    }
    SubShader
    {
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;

            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }

            float4 frag() : SV_Target { return float4(_Color.rgb, 1); }
            ENDCG
        }
    }
}
