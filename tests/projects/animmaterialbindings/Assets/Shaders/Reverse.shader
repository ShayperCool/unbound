Shader "Q758/Reverse"
{
    Properties
    {
        _RFloatA ("FloatA", Float) = 0
        [HDR] _RHdrColor ("HdrColor", Color) = (1,1,1,1)
        [PerRendererData] _RPerRenderer ("PerRenderer", 2D) = "white" {}
        [HideInInspector] _RHidden ("Hidden", Float) = 0
        _RVolume ("Volume", 3D) = "" {}
        _RCube ("Cube", Cube) = "" {}
        [NoScaleOffset] _RTexNoST ("TexNoST", 2D) = "white" {}
        _RTexA ("TexA", 2D) = "white" {}
        _RInteger ("Integer", Integer) = 0
        _RInt ("Int", Int) = 0
        _RVector ("Vector", Vector) = (0,0,0,0)
        _RRange ("Range", Range(0,1)) = 0.5
        _RColor ("Color", Color) = (1,1,1,1)
        _RFloatB ("FloatB", Float) = 0
        _RTexB ("TexB", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
}
