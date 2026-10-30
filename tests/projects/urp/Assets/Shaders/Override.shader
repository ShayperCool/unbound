Shader "Probe/Override"
{
    Properties { _BaseColor ("Color", Color) = (0.8,0.2,0.4,0.6) _BaseMap ("Map", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        HLSLINCLUDE
        #include "UnityCG.cginc"
        float4 _BaseColor;
        sampler2D _BaseMap;
        float4 _BaseMap_ST;
        struct Varyings { float4 position: SV_POSITION; float2 uv: TEXCOORD0; };
        Varyings vertex(appdata_base input)
        {
            Varyings o;
            o.position = UnityObjectToClipPos(input.vertex);
            o.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
            return o;
        }
        float4 first(Varyings i): SV_Target { return float4((_BaseColor * tex2D(_BaseMap, i.uv)).rgb, 0.6); }
        float4 second(Varyings i): SV_Target { return float4(1.0 - (_BaseColor * tex2D(_BaseMap, i.uv)).rgb, 0.6); }
        ENDHLSL
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vertex
            #pragma fragment first
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vertex
            #pragma fragment second
            ENDHLSL
        }
    }
}
