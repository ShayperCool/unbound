Shader "Probe/Props"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.5, 0.25, 1)
        [HDR] _Emis ("Emission", Color) = (0, 0, 0, 0)
        _Vec ("Vector", Vector) = (1, 2, 3, 4)
        _Flt ("Float", Float) = 0.5
        _Rng ("Range", Range(0, 2)) = 1
        _Int ("Int", Int) = 3
        _Integer ("Integer", Integer) = 7
        [NoScaleOffset] _Tex ("Tex", 2D) = "white" {}
        _Tex3 ("Tex3", 3D) = "" {}
        _Cube ("Cube", Cube) = "" {}
        [PerRendererData] _Per ("Per", 2D) = "black" {}
        [Toggle] _Tog ("Toggle", Float) = 1
        [HideInInspector] _Hid ("Hidden", Float) = 0
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
