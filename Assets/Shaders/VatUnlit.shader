Shader "Custom/VatUnlit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [NoScaleOffset] _PositionVAT ("Position VAT", 2D) = "white" {}
        [NoScaleOffset] _NormalVAT ("Normal VAT", 2D) = "white" {}
        _VATWidth ("VAT Width", Float) = 1
        _VATHeight ("VAT Height", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            sampler2D _PositionVAT;
            float _VATWidth;
            float _VATHeight;

            struct InstanceData
            {
                float4x4 transformMatrix;
                float4 animData;
                float4 color;
            };
            StructuredBuffer<InstanceData> _InstanceBuffer;

            v2f vert(appdata v, uint instanceID : SV_InstanceID, uint vertexID : SV_VertexID)
            {
                v2f o;
                InstanceData inst = _InstanceBuffer[instanceID];
                float animFrame = inst.animData.y;
                float clipStart = inst.animData.z;
                float clipFrameCount = max(inst.animData.w, 1.0);

                float u = (vertexID + 0.5) / _VATWidth;
                float localFrame = fmod(animFrame, clipFrameCount);
                float frameBase = floor(localFrame);
                float fracFrame = frac(localFrame);
                float nextFrame = frameBase + 1.0;
                if (nextFrame >= clipFrameCount) nextFrame = 0.0;

                float vBase = (clipStart + frameBase + 0.5) / _VATHeight;
                float vNext = (clipStart + nextFrame + 0.5) / _VATHeight;

                float3 posA = tex2Dlod(_PositionVAT, float4(u, vBase, 0, 0)).xyz;
                float3 posB = tex2Dlod(_PositionVAT, float4(u, vNext, 0, 0)).xyz;
                float3 finalPos = lerp(posA, posB, fracFrame);

                float3 worldPos = mul(inst.transformMatrix, float4(finalPos, 1)).xyz;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = inst.color;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _Color * i.color;
            }
            ENDCG
        }
    }

    Fallback "Custom/VatInstancing"
}
