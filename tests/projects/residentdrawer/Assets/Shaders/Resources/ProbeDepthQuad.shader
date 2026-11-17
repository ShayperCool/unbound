// The motion profile's depth controls: a triangle covering the target whose clip-space depth is
// _Depth (w 1, so the depth buffer holds _Depth), written whatever the target held (ZTest Always).
Shader "Hidden/ResidentDrawerProbe/DepthQuad"
{
    Properties { _Depth ("Depth", Float) = 0.5 }
    SubShader
    {
        Pass
        {
            ZWrite On
            ZTest Always
            Cull Off
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float _Depth;
            float4 vert(float4 position : POSITION) : SV_POSITION { return float4(position.xy, _Depth, 1); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
