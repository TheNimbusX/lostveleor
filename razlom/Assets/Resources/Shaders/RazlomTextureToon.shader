Shader "Razlom/Texture Toon"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0.58,0.66,0.88,1)
        _MidColor ("Mid Color", Color) = (0.94,0.90,0.91,1)
        _MidThreshold ("Mid Threshold", Range(0,1)) = 0.24
        _LightThreshold ("Light Threshold", Range(0,1)) = 0.62
        _LightFeather ("Light Feather", Range(0.001,0.25)) = 0.045
        _RimColor ("Rim Color", Color) = (1,0.86,0.66,1)
        _RimPower ("Rim Power", Range(1,10)) = 4
        _OutlineColor ("Outline Color", Color) = (0.09,0.025,0.075,1)
        _OutlineWidth ("Outline Pixels", Range(0,3)) = 1.10
        _HitFlash ("Hit Flash", Range(0,1)) = 0
        _DeathFade ("Death Fade", Range(0,1)) = 0
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
                half _DeathFade;
            CBUFFER_END

            float4 _RazlomHeroLightPosition;
            half4 _RazlomHeroLightColor;

            half DeathDither(float2 pixelPosition)
            {
                // Stable interleaved gradient noise. Keeping this as an opaque
                // clip preserves depth writes, sorting and SRP batching while
                // still reading as a soft fade at gameplay distance.
                float noise = frac(52.9829189 * frac(dot(
                    floor(pixelPosition), float2(0.06711056, 0.00583715))));
                return 0.0001h + (half)noise * 0.9998h;
            }

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
                clip((1.0h - _DeathFade) - DeathDither(input.positionCS.xy));
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
                // Цвет ключа теперь доходит до светлой стороны почти целиком.
                // Приглушать его на 58% значило гасить единственный тёплый
                // акцент в кадре и делать свет бесцветным.
                keyTint = lerp(half3(1.0h, 0.98h, 0.95h), keyTint, 0.78h);

                // РАЗДЕЛЕНИЕ ПО ТОНУ, А НЕ ПО ЯРКОСТИ.
                //
                // Здесь стояло (0.34, 0.35, 0.39) — нейтрально-серая тень при
                // тёплом ключе и тёплом филле. Все три источника в сцене
                // тёплые, поэтому свет и тень отличались только яркостью, и
                // кадр читался как выцветшая бежевая заливка. Это и есть то,
                // что на записи 1 сентября выглядело мёртвым.
                //
                // У эталона (Genshin) свет тёплый, а тень отчётливо холодная и
                // уходит в сине-фиолетовое. Разница в ЦВЕТЕ, а не в темноте:
                // тень остаётся светлой, но другого тона — оттого картинка
                // читается насыщенной, не теряя деталь в чёрном.
                half3 shadowTone = lerp(half3(0.30h, 0.35h, 0.50h),
                                        _ShadowColor.rgb, 0.24h);
                // Полутон — переход, и он тоже слегка холодный: тёплым он
                // склеивался бы со светлой стороной в одно пятно.
                half3 midTone = lerp(half3(0.70h, 0.72h, 0.78h),
                                     _MidColor.rgb, 0.30h);
                half3 lightTone = keyTint * 1.22h;
                half3 tone = lerp(shadowTone, midTone, midBand);
                tone = lerp(tone, lightTone, lightBand);

                // The atlas already carries hand-painted form. Keep that
                // information and add only a restrained environment fill;
                // multiplying it by a dark two-band light was the source of
                // the dirty, crushed look at gameplay distance.
                // АМБИЕНТ — ЭТО ТО, ЧЕМ ЗАПОЛНЕНА ТЕНЬ.
                //
                // Было 0.08: вклад почти нулевой, и тень держалась только на
                // умножении альбедо. В эталоне тень заполнена холодным светом
                // неба, и именно он не даёт ей провалиться в грязь.
                //
                // Вклад поднят и подкрашен в холодное, причём СИЛЬНЕЕ на
                // теневой стороне: на светлой он смешался бы с ключом и съел
                // тёплый акцент.
                half3 ambient = max(SampleSH(normal), half3(0, 0, 0));
                half3 skyFill = lerp(half3(0.62h, 0.74h, 1.0h), half3(1.0h, 1.0h, 1.0h),
                                     lightBand);
                half ambientAmount = lerp(0.34h, 0.12h, lightBand);

                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));

                // Кромка сужена: pow по _RimPower давал широкий ореол по всему
                // силуэту. У эталона кромка тонкая и живёт на СВЕТЛОЙ стороне —
                // она подчёркивает форму, а не обводит фигуру целиком.
                half rimRaw = saturate(1.0h - dot(normal, viewDir));
                half rim = pow(rimRaw, _RimPower + 2.0h);
                rim *= 0.25h + 0.75h * lightBand;

                half3 color = texel.rgb * (tone + ambient * skyFill * ambientAmount);

                // The Orvill atlas intentionally contains near-black cloth and
                // armour. A small light-side visibility floor keeps those forms
                // readable at gameplay zoom without bleaching Pelag or shadows.
                half albedoLuma = dot(texel.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                half darkMask = 1.0h - smoothstep(0.035h, 0.24h, albedoLuma);
                half facingLift = lerp(0.30h, 1.0h, midBand);
                half3 darkLift = lerp(half3(0.060h, 0.052h, 0.066h),
                                      half3(0.125h, 0.078h, 0.048h), lightBand);
                color += darkLift * darkMask * facingLift;
                color += _RimColor.rgb * rim * (0.095h + 0.105h * lightBand);

                float3 heroVector = _RazlomHeroLightPosition.xyz - input.positionWS;
                float heroDistance = max(length(heroVector), 0.001);
                half3 heroDirection = heroVector / heroDistance;
                half heroAttenuation = saturate(1.0h -
                    heroDistance / max(_RazlomHeroLightPosition.w, 0.001));
                heroAttenuation *= heroAttenuation;
                half heroWrap = saturate(dot(normal, heroDirection) * 0.55h + 0.45h);
                color += texel.rgb * _RazlomHeroLightColor.rgb * heroAttenuation *
                    (0.20h + heroWrap * 0.42h);
                // Near-black armour still needs to catch the combat flash.
                // This small additive lobe is what separates overlapping
                // silhouettes when the textured albedo itself is almost zero.
                color += _RazlomHeroLightColor.rgb * heroAttenuation *
                    (0.025h + heroWrap * 0.085h);
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
                half _DeathFade;
            CBUFFER_END

            half DeathDither(float2 pixelPosition)
            {
                float noise = frac(52.9829189 * frac(dot(
                    floor(pixelPosition), float2(0.06711056, 0.00583715))));
                return 0.0001h + (half)noise * 0.9998h;
            }

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
                float directionLength = length(direction);

                // СИЛУЭТ, А НЕ КАРТА РЁБЕР.
                //
                // Раньше направление здесь нормализовалось безусловно, и каждая
                // вершина уезжала на полную ширину — включая те, чья нормаль
                // смотрит прямо в камеру. У брони таких вершин большинство:
                // плоскость наплечника, пластина юбки, пряжка. Оболочка рвалась
                // на каждом жёстком ребре, и сквозь разрыв проступал контур
                // ВНУТРИ фигуры. Отсюда «много лишних линий» вместо силуэта.
                //
                // Силуэт — это ровно те места, где нормаль перпендикулярна
                // взгляду, то есть где длина её экранной проекции близка к
                // единице. Ей и надо мерить толщину. Степень добавляет порог:
                // полутон гасится, край остаётся.
                float silhouette = directionLength * directionLength * directionLength;
                float2 pixelSize = 2.0 / _ScreenParams.xy;
                output.positionCS.xy += (direction / max(directionLength, 0.0001)) *
                                        silhouette * pixelSize *
                                        _OutlineWidth * output.positionCS.w;
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                clip((1.0h - _DeathFade) - DeathDither(input.positionCS.xy));
                return _OutlineColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

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
                half _DeathFade;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            half DeathDither(float2 pixelPosition)
            {
                float noise = frac(52.9829189 * frac(dot(
                    floor(pixelPosition), float2(0.06711056, 0.00583715))));
                return 0.0001h + (half)noise * 0.9998h;
            }

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                output.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip((1.0h - _DeathFade) - DeathDither(input.positionCS.xy));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

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
                half _DeathFade;
            CBUFFER_END

            half DeathDither(float2 pixelPosition)
            {
                float noise = frac(52.9829189 * frac(dot(
                    floor(pixelPosition), float2(0.06711056, 0.00583715))));
                return 0.0001h + (half)noise * 0.9998h;
            }

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip((1.0h - _DeathFade) - DeathDither(input.positionCS.xy));
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
