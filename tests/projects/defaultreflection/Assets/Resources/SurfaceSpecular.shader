// A surface shader lit by the StandardSpecular model: a smooth plain surface with a specular color.
Shader "Probe/SurfaceSpecular" {
    Properties {
        _Color ("Color", Color) = (0.1, 0.1, 0.1, 1)
        _SpecColor ("Specular", Color) = (0.8, 0.8, 0.8, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.95
    }
    SubShader {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf StandardSpecular
        #pragma target 3.0
        fixed4 _Color;
        half _Glossiness;
        struct Input { float3 worldPos; };
        void surf (Input IN, inout SurfaceOutputStandardSpecular o)
        {
            o.Albedo = _Color.rgb;
            o.Specular = _SpecColor.rgb;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
}
