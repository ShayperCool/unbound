// A surface shader: Unity generates its passes and their keyword sets.
Shader "Probe/KwSurface"
{
    Properties { _Color ("Color", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert
        #pragma shader_feature_local _ SURF_LOCAL
        #pragma multi_compile _ SURF_GLOBAL
        fixed4 _Color;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutput o) { o.Albedo = _Color.rgb; }
        ENDCG
    }
}
