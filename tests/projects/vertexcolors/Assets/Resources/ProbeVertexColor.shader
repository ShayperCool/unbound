// Writes the interpolated vertex color unchanged: what the renderer put in the vertices.
Shader "Probe/VertexColor"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; float4 color : COLOR; };
            struct Output { float4 vertex : SV_POSITION; float4 color : COLOR; };
            Output vert(Input input) { Output output; output.vertex = UnityObjectToClipPos(input.vertex); output.color = input.color; return output; }
            float4 frag(Output input) : SV_Target { return input.color; }
            ENDCG
        }
    }
}
