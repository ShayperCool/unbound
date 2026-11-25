Shader "Q758/Forward"
{
    Properties
    {
        _QTexB ("TexB", 2D) = "white" {}
        _QFloatB ("FloatB", Float) = 0
        _QColor ("Color", Color) = (1,1,1,1)
        _QRange ("Range", Range(0,1)) = 0.5
        _QVector ("Vector", Vector) = (0,0,0,0)
        _QInt ("Int", Int) = 0
        _QInteger ("Integer", Integer) = 0
        _QTexA ("TexA", 2D) = "white" {}
        [NoScaleOffset] _QTexNoST ("TexNoST", 2D) = "white" {}
        _QCube ("Cube", Cube) = "" {}
        _QVolume ("Volume", 3D) = "" {}
        [HideInInspector] _QHidden ("Hidden", Float) = 0
        [PerRendererData] _QPerRenderer ("PerRenderer", 2D) = "white" {}
        [HDR] _QHdrColor ("HdrColor", Color) = (1,1,1,1)
        _QFloatA ("FloatA", Float) = 0
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
