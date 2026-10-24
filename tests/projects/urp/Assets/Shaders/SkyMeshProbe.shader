// Draws the sky with each triangle flat in its provoking vertex's object-space position (and its
// world distance from the camera in alpha): read back, the colors give the sky mesh's vertices.
Shader "Hidden/Unbound/SkyMeshProbe"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; nointerpolation float4 data : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.data = float4(v.vertex.xyz, length(world - _WorldSpaceCameraPos));
                return o;
            }
            float4 frag(v2f i) : SV_Target { return i.data; }
            ENDCG
        }
    }
}
