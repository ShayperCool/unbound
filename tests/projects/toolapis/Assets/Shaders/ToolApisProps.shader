// Every property type, with names whose ordinal, case-insensitive and declaration orders differ
// (tests/projects/toolapis matx.*).
Shader "ToolApis/Props"
{
    Properties
    {
        _Zeta("Zeta", Float) = 2.5
        _Alpha("Alpha", Range(0, 1)) = 0.25
        _glossy("Lower", Float) = 1
        _Glossiness("Glossiness", Float) = 0.5
        _GlossMapScale("Gloss map scale", Float) = 0.75
        Bare("Bare", Float) = 6
        _Legacy("Legacy int", Int) = 3
        _Count("Count", Integer) = 4
        _Steps("Steps", Integer) = 0
        [Toggle(_TOGGLE_ON)] _Toggle("Toggle", Float) = 1
        _Tint("Tint", Color) = (1, 0, 0, 1)
        _Dir("Dir", Vector) = (1, 2, 3, 4)
        _BaseMap("Base", 2D) = "white" {}
        _Bump("Bump", 2D) = "bump" {}
        _Cube("Cube", Cube) = "" {}
        _Vol("Vol", 3D) = "" {}
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature _TOGGLE_ON
            #pragma multi_compile _ _MODE_A _MODE_B
            #include "UnityCG.cginc"
            sampler2D _BaseMap; float4 _Tint; float _Zeta;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target
            {
                float4 c = tex2D(_BaseMap, 0) * _Tint * _Zeta;
            #if _TOGGLE_ON
                c *= 0.5;
            #endif
            #if _MODE_A
                c.r = 1;
            #endif
                return c;
            }
            ENDCG
        }
    }
}
