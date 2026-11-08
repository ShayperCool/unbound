Shader "Hidden/RenderApiStreams"
{
    SubShader
    {
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 position:POSITION; float4 color:COLOR; float4 uv1:TEXCOORD1; float4 uv2:TEXCOORD2; float4 uv3:TEXCOORD3; };
            struct Output { float4 position:SV_POSITION; float4 color:COLOR0; };
            Output vert(Input i) { Output o; o.position=UnityObjectToClipPos(i.position); o.color=i.color*float4(i.uv1.x,i.uv2.y,i.uv3.x,1); return o; }
            float4 frag(Output i):SV_Target { return i.color; }
            ENDCG
        }
    }
}
