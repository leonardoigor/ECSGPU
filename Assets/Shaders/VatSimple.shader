Shader "Custom/VatSimple"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _EmissionMap ("Emission", 2D) = "black" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0,0)

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
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float4 color : COLOR0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            sampler2D _MetallicGlossMap;
            float _Metallic;
            float _Glossiness;
            sampler2D _EmissionMap;
            fixed4 _EmissionColor;
            sampler2D _PositionVAT;
            sampler2D _NormalVAT;
            float _VATWidth;
            float _VATHeight;

            float3 _CustomLightDir;
            float4 _CustomLightColor;

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

                float3 normA = tex2Dlod(_NormalVAT, float4(u, vBase, 0, 0)).xyz;
                float3 normB = tex2Dlod(_NormalVAT, float4(u, vNext, 0, 0)).xyz;
                float3 finalNorm = lerp(normA, normB, fracFrame) * 2.0 - 1.0;

                float3 worldPos = mul(inst.transformMatrix, float4(finalPos, 1)).xyz;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = worldPos;
                o.worldNormal = normalize(mul((float3x3)inst.transformMatrix, finalNorm));
                o.color = inst.color;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color * i.color;
                float3 worldNormal = normalize(i.worldNormal);

                float4 metallicGloss = tex2D(_MetallicGlossMap, i.uv);
                float metallic = metallicGloss.r * _Metallic;
                float smoothness = metallicGloss.a * _Glossiness;

                fixed3 emission = tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb;

                float3 viewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
                float3 lightDir = normalize(_CustomLightDir);
                float3 lightColor = _CustomLightColor.rgb;

                float ndotl = saturate(dot(worldNormal, lightDir));
                fixed3 diffuse = albedo.rgb * lightColor * ndotl;

                float3 halfDir = normalize(lightDir + viewDir);
                float ndoth = saturate(dot(worldNormal, halfDir));
                float specPower = exp2(smoothness * 10.0 + 1.0);
                float spec = pow(ndoth, specPower) * (smoothness + 1.0) * 0.125;

                float3 ambient = ShadeSH9(float4(worldNormal, 1.0)) * albedo.rgb;

                fixed3 color = ambient + diffuse * (1.0 - metallic * 0.5) + spec * lightColor + emission;

                return fixed4(color, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual
            ColorMask 0

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 4.5
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"

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

            struct v2fShadow
            {
                V2F_SHADOW_CASTER;
            };

            v2fShadow vertShadow(appdata_base v, uint instanceID : SV_InstanceID, uint vertexID : SV_VertexID)
            {
                v2fShadow o;
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
                v.vertex = mul(unity_WorldToObject, float4(worldPos, 1));
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 fragShadow(v2fShadow i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    Fallback "Custom/VatInstancing"
}
