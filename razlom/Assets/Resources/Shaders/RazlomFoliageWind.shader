Shader "Razlom/Foliage Wind"
{
    // ЛИСТВА, КОТОРАЯ КАЧАЕТСЯ. Тот же toon-разбор света, что у персонажей в
    // RazlomTextureToon, плюс вершинный ветер из RazlomFoliageWind.hlsl.
    //
    // ЧЕГО ЗДЕСЬ НАМЕРЕННО НЕТ:
    //   * растворения и вспышки попадания — дерево не умирает и не получает
    //     урона, а лишний clip в трёх проходах стоит денег на каждом кусте;
    //   * прохода обводки — контур в игре включается только на враждебных
    //     целях через MaterialPropertyBlock, и декорация в него не входит.
    //
    // ЧЕГО ЗДЕСЬ ПОКА НЕТ, НО ПОНАДОБИТСЯ: alpha clip. У этого дерева листья —
    // честная геометрия, а базовая карта лежит в JPEG, где альфы нет вовсе.
    // Как только появятся карточки травы с вырезом, во ВСЕ ТРИ прохода
    // добавляется одинаковый clip(alpha - _Cutoff) — в три, а не в один,
    // иначе тень и глубина останутся прямоугольными.
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

        [Header(Veter)]
        // x — объектный Y, до которого ствол стоит намертво (он же ось
        // наклона), y — Y верхушки, zw — X и Z центра ствола. Ставится замером
        // по мешу из меню «Разлом → Листва», вручную сюда лезть незачем.
        _WindHeightRange ("Основание и верх (объектные Y, XZ ствола)", Vector) = (0,1,0,0)
        // АМПЛИТУДЫ В ГРАДУСАХ, А НЕ В МЕТРАХ, потому что оба слоя движения —
        // повороты. Метры были у трансляции, а трансляция и давала желе.
        //
        // Два градуса на четырёхметровом дереве — это сантиметров пятнадцать
        // хода верхушки. Больше пяти читается уже как шторм.
        _WindSwayAngle ("Наклон кроны, градусы", Range(0,12)) = 2.2
        _WindSwayFrequency ("Частота наклона, Гц", Range(0.02,1)) = 0.16
        // Поворот листовой шапки вокруг собственного центра. Форма шапки при
        // этом не меняется вообще — она дёргается, а не раздувается.
        _WindFlutterAngle ("Поворот листовой шапки, градусы", Range(0,15)) = 3.5
        _WindFlutterFrequency ("Частота трепета, Гц", Range(0.5,8)) = 2.4
        _WindGust ("Глубина порыва", Range(0,1)) = 0.55
        // САМАЯ ВАЖНАЯ РУЧКА ПОСЛЕ АМПЛИТУДЫ. Размер листовой шапки в объектных
        // единицах: вершины внутри одной клетки этого размера трепещут в одной
        // фазе, соседние клетки — в несвязанных.
        //
        // Слишком мелко — фазы расходятся ВНУТРИ одного листа, и его рвёт.
        // Слишком крупно — вся крона снова становится одной плитой.
        // Ставится замером по мешу из меню «Разлом → Листва».
        _WindClusterSize ("Размер листовой шапки, объектные ед.", Range(0.05,3)) = 0.45
        // Насколько расходятся фазы НАКЛОНА у разных веток. Ноль — крона едет
        // вбок одним куском. Единица — ветки качаются вразнобой, и дерево
        // перестаёт читаться как одно целое. Правда посередине и ближе к нулю.
        _WindBranchScatter ("Разброс фазы веток", Range(0,1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardFoliage"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RazlomFoliageWind.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float4 _RazlomHeroLightPosition;
            half4 _RazlomHeroLightColor;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = RazlomFoliageWorldPosition(input.positionOS.xyz);
                VertexPositionInputs position = RazlomFoliagePositionInputs(positionWS);

                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                // НОРМАЛЬ ВЕТЕР НЕ КРУТИТ, И ЭТО ОСОЗНАННО. Честный поворот
                // нормали стоил бы производной сдвига по поверхности, а на
                // амплитуде в сантиметры разница в затенении меньше ступени
                // cel-разбора: банда всё равно не переключится.
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.shadowCoord = GetShadowCoord(position);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normal = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);
                half ndl = saturate(dot(normal, mainLight.direction));
                half shadeInput = ndl * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half midBand = smoothstep(_MidThreshold - _LightFeather,
                                          _MidThreshold + _LightFeather, shadeInput);
                half lightBand = smoothstep(_LightThreshold - _LightFeather,
                                            _LightThreshold + _LightFeather, shadeInput);

                // Разбор света слово в слово повторяет RazlomTextureToon: один
                // тёплый ключ, холодная тень, полутон посередине. Дерево стоит
                // в том же кадре, что и персонажи, и своя формула света
                // означала бы, что оно из другой игры.
                half maxKeyChannel = max(max(mainLight.color.r, mainLight.color.g),
                                         max(mainLight.color.b, 0.001h));
                half3 keyTint = mainLight.color / maxKeyChannel;
                keyTint = lerp(half3(1.0h, 0.98h, 0.95h), keyTint, 0.78h);

                half3 shadowTone = lerp(half3(0.38h, 0.39h, 0.43h), _ShadowColor.rgb, 0.16h);
                half3 midTone = lerp(half3(0.74h, 0.73h, 0.72h), _MidColor.rgb, 0.30h);
                half3 lightTone = keyTint * 1.22h;
                half3 tone = lerp(shadowTone, midTone, midBand);
                tone = lerp(tone, lightTone, lightBand);

                half3 ambient = max(SampleSH(normal), half3(0, 0, 0));
                half ambientAmount = lerp(0.26h, 0.10h, lightBand);

                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half rimRaw = saturate(1.0h - dot(normal, viewDir));
                half rim = pow(rimRaw, _RimPower + 2.0h);
                rim *= 0.25h + 0.75h * lightBand;

                half3 color = texel.rgb * (tone + ambient * ambientAmount);
                color += _RimColor.rgb * rim * (0.095h + 0.105h * lightBand);

                // Тёплый свет от героя. Пол арены его уже читает
                // (RazlomArenaFloor), и листва обязана тоже: дерево, не
                // реагирующее на источник, который освещает землю под ним,
                // выглядит наклейкой. В лагере герой без света — вклад нулевой.
                float3 heroVector = _RazlomHeroLightPosition.xyz - input.positionWS;
                float heroDistance = max(length(heroVector), 0.001);
                half3 heroDirection = heroVector / heroDistance;
                half heroAttenuation = saturate(1.0h -
                    heroDistance / max(_RazlomHeroLightPosition.w, 0.001));
                heroAttenuation *= heroAttenuation;
                half heroWrap = saturate(dot(normal, heroDirection) * 0.55h + 0.45h);
                color += texel.rgb * _RazlomHeroLightColor.rgb * heroAttenuation *
                    (0.20h + heroWrap * 0.42h);

                color = MixFog(color, input.fogFactor);
                return half4(color, texel.a);
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
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "RazlomFoliageWind.hlsl"

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

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // ТОТ ЖЕ САМЫЙ СДВИГ, ЧТО И В ЦВЕТОВОМ ПРОХОДЕ.
                //
                // Пропусти его здесь — и крона будет качаться, а её тень на
                // земле стоять колом. Это самая заметная поломка вершинного
                // ветра и самая частая: цветовой проход правят, а два
                // служебных забывают.
                float3 positionWS = RazlomFoliageWorldPosition(input.positionOS.xyz);
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
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RazlomFoliageWind.hlsl"

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

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // И здесь тоже. Глубина обязана совпадать с цветовым проходом:
                // на ней стоят SSAO и всё, что читает depth-текстуру, и
                // несдвинутая крона даст ореол вокруг качающейся листвы.
                float3 positionWS = RazlomFoliageWorldPosition(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
