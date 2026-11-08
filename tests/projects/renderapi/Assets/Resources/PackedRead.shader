Shader "Hidden/RenderApiPacked"
{
    Properties { _UseUvs ("Use UVs", Float) = 1 }
    SubShader
    {
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _UseUvs;
            struct Input { float4 position:POSITION; float4 color:COLOR; float4 uv:TEXCOORD0; float4 extra:TEXCOORD5; };
            struct Output { float4 position:SV_POSITION; float4 color:COLOR0; };
            Output vert(Input i) { Output o; o.position=UnityObjectToClipPos(i.position); o.color=i.color*lerp(float4(1,1,1,1),float4(i.uv.xy,i.extra.x,1),_UseUvs); return o; }
            float4 frag(Output i):SV_Target { return i.color; }
            ENDCG
        }
    }
}
