Shader "Probe/ModelBlend"
{
    Properties
    {
        _Mode ("Mode", Float) = 0
        _SrcBlend ("Source blend", Float) = 1
        _DstBlend ("Destination blend", Float) = 0
        _ZWrite ("Depth write", Float) = 1
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionColor ("Emission", Color) = (0,0,0,0)
        _BumpMap ("Normal", 2D) = "bump" {}
        _DetailNormalMap ("Detail normal", 2D) = "bump" {}
        _ParallaxMap ("Height", 2D) = "black" {}
        _DetailAlbedoMap ("Detail color", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag() : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
    CustomEditor "StandardShaderGUI"
}
