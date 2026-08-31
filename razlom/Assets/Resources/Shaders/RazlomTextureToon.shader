Shader "Razlom/Texture Toon"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0.80,0.72,0.76,1)
        _MidColor ("Mid Color", Color) = (0.94,0.90,0.91,1)
        _MidThreshold ("Mid Threshold", Range(0,1)) = 0.24
        _LightThreshold ("Light Threshold", Range(0,1)) = 0.62
        _LightFeather ("Light Feather", Range(0.001,0.25)) = 0.045
        _RimColor ("Rim Color", Color) = (1,0.48,0.34,1)
        _RimPower ("Rim Power", Range(1,10)) = 4
        _OutlineColor ("Outline Color", Color) = (0.09,0.025,0.075,1)
        _OutlineWidth ("Outline Pixels", Range(0,3)) = 1.10
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
            #pragma multi_compile_instancing
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
                half4 _MidColor;
                half _MidThreshold;
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
                half midBand = smoothstep(_MidThreshold - _LightFeather,
                                          _MidThreshold + _LightFeather, shadeInput);
                half lightBand = smoothstep(_LightThreshold - _LightFeather,
                                            _LightThreshold + _LightFeather, shadeInput);
                // One authoritative warm key. In URP 17/Forward+ the main
                // directional is stable, while depending on additional-light
                // loops for the character look makes variants/platforms drift.
                half maxKeyChannel = max(max(mainLight.color.r, mainLight.color.g),
                                         max(mainLight.color.b, 0.001h));
                half3 keyTint = mainLight.color / maxKeyChannel;
                keyTint = lerp(half3(1.0h, 0.98h, 0.95h), keyTint, 0.42h);

                half3 shadowTone = lerp(half3(0.34h, 0.35h, 0.39h),
                                        _ShadowColor.rgb, 0.24h);
                half3 midTone = lerp(half3(0.74h, 0.72h, 0.70h),
                                     _MidColor.rgb, 0.30h);
                half3 lightTone = keyTint * 1.20h;
                half3 tone = lerp(shadowTone, midTone, midBand);
                tone = lerp(tone, lightTone, lightBand);

                // The atlas already carries hand-painted form. Keep that
                // information and add only a restrained environment fill;
                // multiplying it by a dark two-band light was the source of
                // the dirty, crushed look at gameplay distance.
                half3 ambient = max(SampleSH(normal), half3(0, 0, 0));
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(saturate(1.0h - dot(normal, viewDir)), _RimPower);
                half3 color = texel.rgb * (tone + ambient * 0.08h);

                // The Orvill atlas intentionally contains near-black cloth and
                // armour. A small light-side visibility floor keeps those forms
                // readable at gameplay zoom without bleaching Pelag or shadows.
                half albedoLuma = dot(texel.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                half darkMask = 1.0h - smoothstep(0.035h, 0.24h, albedoLuma);
                half facingLift = lerp(0.30h, 1.0h, midBand);
                half3 darkLift = lerp(half3(0.036h, 0.031h, 0.028h),
                                      half3(0.075h, 0.055h, 0.040h), lightBand);
                color += darkLift * darkMask * facingLift;
                color += _RimColor.rgb * rim * (0.055h + 0.065h * lightBand);
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
            #pragma multi_compile_instancing
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
                half4 _MidColor;
                half _MidThreshold;
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
                output.positionCS = TransformWorldToHClip(positionWS);

                // Constant screen-space thickness. World-space extrusion made
                // the dense hair/clothes outline crawl with camera distance.
                float3 normalVS = TransformWorldToViewDir(normalWS, true);
                float2 direction = normalVS.xy;
                float directionLength = max(length(direction), 0.0001);
                float2 pixelSize = 2.0 / _ScreenParams.xy;
                output.positionCS.xy += (direction / directionLength) * pixelSize *
                                        _OutlineWidth * output.positionCS.w;
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
