Shader "Razlom/Texture Toon"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0.36,0.20,0.32,1)
        _LightThreshold ("Light Threshold", Range(0,1)) = 0.48
        _LightFeather ("Light Feather", Range(0.001,0.25)) = 0.055
        _RimColor ("Rim Color", Color) = (1,0.48,0.34,1)
        _RimPower ("Rim Power", Range(1,10)) = 4
        _OutlineColor ("Outline Color", Color) = (0.09,0.025,0.075,1)
        _OutlineWidth ("Outline Width", Range(0,0.03)) = 0.007
        _HitFlash ("Hit Flash", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardToon"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half _LightThreshold;
                half _LightFeather;
                half4 _RimColor;
                half _RimPower;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _HitFlash;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.shadowCoord = GetShadowCoord(position);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normal = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);
                half ndl = saturate(dot(normal, mainLight.direction));
                half shadeInput = ndl * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half band = smoothstep(_LightThreshold - _LightFeather,
                                       _LightThreshold + _LightFeather, shadeInput);
                half3 litTone = lerp(_ShadowColor.rgb, mainLight.color, band);
                half3 ambient = SampleSH(normal) * 0.22h;
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(saturate(1.0h - dot(normal, viewDir)), _RimPower) * band;
                half3 color = texel.rgb * (litTone + ambient) + _RimColor.rgb * rim * 0.34h;
                color = lerp(color, half3(1.0h, 0.88h, 0.58h), saturate(_HitFlash));
                color = MixFog(color, input.fogFactor);
                return half4(color, texel.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "InkOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings { float4 positionCS : SV_POSITION; };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half _LightThreshold;
                half _LightFeather;
                half4 _RimColor;
                half _RimPower;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _HitFlash;
            CBUFFER_END

            Varyings OutlineVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(positionWS + normalWS * _OutlineWidth);
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
