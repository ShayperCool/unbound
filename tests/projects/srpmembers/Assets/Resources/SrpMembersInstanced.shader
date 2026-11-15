// An unlit instanced red.
Shader "Hidden/SrpMembersInstanced"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i) : SV_Target { return fixed4(1, 0, 0, 1); }
            ENDCG
        }
    }
}
