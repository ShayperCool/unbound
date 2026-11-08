Shader "Hidden/RenderApi/SkinNormal" {
    SubShader { Pass { Cull Off
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"
        struct Input { float4 vertex : POSITION; float3 normal : NORMAL; };
        struct Varying { float4 position : SV_POSITION; float3 normal : TEXCOORD0; };
        Varying vert(Input v) { Varying o; o.position=UnityObjectToClipPos(v.vertex); o.normal=UnityObjectToWorldNormal(v.normal); return o; }
        float4 frag(Varying i) : SV_Target {
            float intensity=max(dot(normalize(i.normal),normalize(float3(.3,.8,-.4))),0);
            return float4(intensity,intensity,intensity,1);
        }
        ENDCG
    } }
}
