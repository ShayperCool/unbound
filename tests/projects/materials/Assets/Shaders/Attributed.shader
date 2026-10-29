// Main texture and colour named by attributes, no _MainTex or _Color.
Shader "Probe/Attributed"
{
    Properties
    {
        [MainTexture] _BaseMap("Base", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (0.25, 0.5, 0.75, 1)
        _Gloss("Gloss", Range(0, 1)) = 0.5
        _Count("Count", Integer) = 7
        _Dir("Direction", Vector) = (1, 2, 3, 4)
        _SecondTex("Second", 2D) = "black" {}
        [HideInInspector][NoScaleOffset] _Hidden("Hidden", 2D) = "gray" {}
        [Toggle(_FEATURE_ON)] _Feature("Feature", Float) = 0
        [HDR] _Glow("Glow", Color) = (2, 1, 1, 1)
        _Legacy("Legacy", Int) = 3
        [Normal] _Bump("Bump", 2D) = "bump" {}
        [Gamma] _Gam("Gamma", Float) = 1
        [PerRendererData] _Prd("Per renderer", 2D) = "white" {}
        [Header(A header)] [Space(4)] [Enum(One, 1, Two, 2)] _Choice("Choice", Float) = 2
        [IntRange] _Steps("Steps", Range(0, 8)) = 4
        [KeywordEnum(Low, High)] _Quality("Quality", Float) = 0
        [Toggle] _OnByDefault("On by default", Float) = 1
        [ToggleOff] _Off("Off", Float) = 1
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _BaseMap; sampler2D _SecondTex; float4 _BaseColor; float _Gloss; float4 _Dir;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return tex2D(_BaseMap, 0) * _BaseColor * _Gloss + tex2D(_SecondTex, 0) + _Dir; }
            ENDCG
        }
    }
}
