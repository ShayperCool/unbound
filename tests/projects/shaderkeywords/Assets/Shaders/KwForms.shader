// Every keyword declaration form: global and local, multi_compile and shader_feature, stage
// suffixes, dynamic_branch, "_" and "__" placeholders, built-in sets and names, a keyword declared
// global in one pass and local in another, declarations from an included file, a conditional
// declaration, and a second SubShader for another pipeline.
Shader "Probe/KwForms"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0, 0, 1)
        _Value ("Value", Float) = 0.5
        _Other ("Other", Range(0, 2)) = 1
        _MainTex ("Texture", 2D) = "white" {}
        [Toggle(TOGGLE_KW)] _Toggle ("Toggle", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+5" "ProbeTag"="first" }
        LOD 300
        Pass
        {
            Name "MAIN"
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ GLOBAL_A GLOBAL_B
            #pragma multi_compile_local _ LOCAL_A
            #pragma multi_compile_local __ LOCAL_B LOCAL_C
            #pragma shader_feature FEATURE_A
            #pragma shader_feature_local _ FEATURE_LOCAL_A FEATURE_LOCAL_B
            #pragma shader_feature_local_fragment FRAG_LOCAL
            #pragma multi_compile_vertex _ VTX_GLOBAL
            #pragma dynamic_branch DYN_GLOBAL
            #pragma dynamic_branch_local DYN_LOCAL_A DYN_LOCAL_B
            #pragma dynamic_branch_local_fragment DYN_FRAG
            #pragma multi_compile _ SHARED_KW
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma shader_feature TOGGLE_KW
            #pragma skip_variants GLOBAL_B
            #if defined(SHADER_API_D3D11)
            #pragma multi_compile _ D3D_ONLY
            #endif
            #include_with_pragmas "KwPragmas.cginc"
            #include "KwPlainInclude.cginc"
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
        Pass
        {
            Name "SECOND"
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ SHARED_KW
            #pragma multi_compile_local _ PASS2_ONLY
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return 0; }
            ENDCG
        }
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "ProbeTag"="second" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ SUB2_GLOBAL
            #pragma multi_compile_local _ LOCAL_A
            float4 vert(float4 v : POSITION) : SV_POSITION { return v; }
            float4 frag() : SV_Target { return 1; }
            ENDHLSL
        }
        Pass
        {
            Name "EXTRA"
            ZWrite Off
        }
    }
    Fallback "Unlit/Color"
}
