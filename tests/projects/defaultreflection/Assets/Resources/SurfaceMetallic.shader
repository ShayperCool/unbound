// A surface shader lit by the Standard model: a smooth plain surface whose metallic value is a property.
Shader "Probe/SurfaceMetallic" {
    Properties {
        _Color ("Color", Color) = (0.9, 0.9, 0.9, 1)
        _Metallic ("Metallic", Range(0, 1)) = 1
        _Glossiness ("Smoothness", Range(0, 1)) = 0.95
    }
    SubShader {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Standard
        #pragma target 3.0
        fixed4 _Color;
        half _Metallic, _Glossiness;
        struct Input { float3 worldPos; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = _Color.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
}
