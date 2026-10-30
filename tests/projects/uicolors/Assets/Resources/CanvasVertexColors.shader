Shader "Probe/CanvasVertexColors"
{
    Properties { _ProbeUniform ("Probe uniform", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            int _UIVertexColorAlwaysGammaSpace;
            float _ProbeUniform;
            struct Input { float4 vertex : POSITION; float4 color : COLOR; };
            struct Output { float4 vertex : SV_POSITION; float4 color : COLOR; };
            Output vert(Input input) { Output output; output.vertex=UnityObjectToClipPos(input.vertex); output.color=input.color; return output; }
            float4 frag(Output input) : SV_Target { return _ProbeUniform > .5 ? float4(_UIVertexColorAlwaysGammaSpace, 0, 0, 1) : input.color; }
            ENDCG
        }
    }
}
