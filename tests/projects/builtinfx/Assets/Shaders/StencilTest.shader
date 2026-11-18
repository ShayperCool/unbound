// A transparent-queue pass drawn where the stencil, masked with _Mask, equals _Ref: white.
Shader "Probe/StencilTest"
{
    Properties { _Ref ("Ref", Float) = 0 _Mask ("Mask", Float) = 255 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            Stencil { Ref [_Ref] ReadMask [_Mask] Comp Equal }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag() : SV_Target { return fixed4(1, 1, 1, 1); }
            ENDCG
        }
    }
}
