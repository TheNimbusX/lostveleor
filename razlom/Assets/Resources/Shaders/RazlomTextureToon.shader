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
        _OutlineWidth ("Outline Pixels", Range(0,3)) = 0
        // ГЛУБИННЫЙ ОТСТУП ОБВОДКИ — ЭТО НЕ ТОНКАЯ НАСТРОЙКА, А ЕЁ СМЫСЛ.
        //
        // Обводка рисуется вывернутой оболочкой: Cull Front поверх уже
        // записанной глубины тела. На ОДНОЙ замкнутой поверхности это даёт
        // ровно силуэт. Но у мобов меш собран из кусков, которые входят друг
        // в друга — шерсть в торс, рога в череп, лапы в юбку, — и раздутая
        // оболочка внутреннего куска вылезает сквозь наружный. Каждый такой
        // выход и есть «обводка захватывает внутренние детали»: это не швы
        // нормалей (их уже лечит сшитая нормаль в тангенсе), а честная
        // геометрия, торчащая на доли миллиметра.
        //
        // Отступ уводит оболочку ОТ КАМЕРЫ. Внутренний прорыв опережает
        // поверхность тела на считанные миллиметры и после отступа не проходит
        // ZTest. Настоящему силуэту это безразлично: за ним нет тела вообще,
        // отодвигать его не обо что.
        //
        // ОТСТУП В МЕТРАХ, А НЕ В ЕДИНИЦАХ ГЛУБИНЫ. Камера тут ортографическая,
        // и сдвиг clip-space z пересчитался бы в мировые метры через всю
        // дальность отсечения — то есть в единицы, а не в сантиметры, и
        // обводку срезало бы целиком. Сдвиг в мировом пространстве одинаково
        // честен и для орто, и для перспективы.
        //
        // Настоящему силуэту отступ ничем не грозит: за ним фон, а не тело.
        // Опасен только перебор у самых ступней, где сзади уже пол.
        // Слишком большой отступ уводит оболочку за тело и при ZTest LEqual
        // съедает весь контур. Оставляем её в глубине только на доли миллиметра.
        _OutlineDepthBias ("Отступ обводки вглубь, м", Range(0,0.3)) = 0.0
        _GroundGlowBand ("Высота контактного свечения", Range(0.02,0.6)) = 0.16
        _GroundGlowFeather ("Мягкость контактного свечения", Range(0.005,0.3)) = 0.06
        _HitFlash ("Hit Flash", Range(0,1)) = 0
        _DeathFade ("Death Fade", Range(0,1)) = 0

        [Header(Dissolve)]
        _DissolveEdgeColor ("Цвет кромки растворения", Color) = (1,0.42,0.12,1)
        _DissolveEdgeGlow ("Яркость кромки", Range(0,8)) = 2.6
        _DissolveEdgeWidth ("Ширина кромки", Range(0.01,0.5)) = 0.14
        _DissolveScale ("Частота шума", Range(2,40)) = 13
        _DissolveVoronoi ("Облака (0) или Вороной (1)", Range(0,1)) = 1
        // СВИП ПО ВЫСОТЕ ВЫКЛЮЧЕН, И ЭТО НЕ ЗАБЫТАЯ НАСТРОЙКА.
        //
        // Он стоял на 0.35 — тело уходило снизу вверх, «как зола». На экране
        // это читалось не золой, а провалом сквозь пол: ноги пропадают, торс
        // ещё цел, контактная тень всё это время лежит на земле целиком. Мозг
        // достраивает единственное знакомое объяснение — моб тонет в полу.
        //
        // Без свипа тело осыпается равномерно по объёму, и никакого «вниз» в
        // кадре не появляется. Ручка оставлена — это Cutoff Height из графа, —
        // но по умолчанию она молчит.
        _DissolveHeightBias ("Подмес свипа по высоте", Range(0,1)) = 0
        // Читается только когда свип выше нуля. Объектные координаты низа и
        // верха тела: у наших персонажей начало координат в ступнях, а меш
        // ростом 0.978 юнита (см. globalScale в RazlomCharacterImport) —
        // отсюда 0..1. Меш с центром в тазу потребует (-0.5, 0.5), иначе
        // половина тела получит одинаковую высоту и уйдёт не свипом, а разом.
        _DissolveHeightRange ("Низ и верх тела (объектные Y)", Vector) = (0,1,0,0)
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
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            // 3.5 = полноценный ps_4_0. По умолчанию Unity берёт 2.5, то есть
            // ps_4_0_level_9_3: там нет динамических ветвлений и стоит потолок
            // по инструкциям, а растворение внутри крутит 27 итераций Вороного
            // и выходит из них по _DeathFade. Тот же потолок в трёх проходах
            // ниже — они стояли на target 2.0.
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // SSAO
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "RazlomDissolve.hlsl"

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
                float3 positionOS : TEXCOORD5;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float4 _RazlomHeroLightPosition;
            half4 _RazlomHeroLightColor;

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
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half dissolveFront = RazlomDissolveFront(input.positionOS);
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normal = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);


                half ndl = saturate(dot(normal, mainLight.direction));

                half directLight =
                    ndl * mainLight.distanceAttenuation;

                half shadowAttenuation =
                    saturate(mainLight.shadowAttenuation);

                half shadeInput = directLight;

                half midBand = smoothstep(
                    _MidThreshold - _LightFeather,
                    _MidThreshold + _LightFeather,
                    shadeInput
                );

                half lightBand = smoothstep(
                    _LightThreshold - _LightFeather,
                    _LightThreshold + _LightFeather,
                    shadeInput
                );

                half maxKeyChannel = max(
                    max(mainLight.color.r, mainLight.color.g),
                    max(mainLight.color.b, 0.001h)
                );

                half3 keyTint = mainLight.color / maxKeyChannel;

                keyTint = lerp(
                    half3(1.0h, 0.98h, 0.95h),
                    keyTint,
                    0.78h
                );

                half3 shadowTone = lerp(
                    half3(0.38h, 0.39h, 0.43h),
                    _ShadowColor.rgb,
                    0.16h
                );

                half3 midTone = lerp(
                    half3(0.74h, 0.73h, 0.72h),
                    _MidColor.rgb,
                    0.30h
                );

                half3 lightTone = keyTint * 1.22h;

                half3 tone = lerp(shadowTone, midTone, midBand);
                tone = lerp(tone, lightTone, lightBand);

                // REALTIME CAST SHADOW
                /*
                half castShadow =
                    smoothstep(0.15h, 0.90h, shadowAttenuation);

                half shadowFloor = 0.12h;

                tone = lerp(
                    shadowTone,
                    tone,
                    lerp(shadowFloor, 1.0h, castShadow)
                );
                */
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
                // ============================================================
                // SCREEN SPACE AMBIENT OCCLUSION
                // ============================================================

                // Экранные UV текущего пикселя.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // Unity SSAO.
                // 1 = открытая поверхность
                // 0 = сильная окклюзия / контакт
                half ssao = SampleAmbientOcclusion(screenUV);

                // Не делаем AO чёрным.
                // Даже при сильной окклюзии сохраняем 45% ambient.
                // Для painterly ARPG это гораздо чище.
                half painterlyAO = lerp(
                    0.45h,
                    1.0h,
                    ssao
                );

                // Обычный environment/sky ambient.
                half3 ambient = max(
                    SampleSH(normal),
                    half3(0, 0, 0)
                );

                // AO режет именно окружающий заполняющий свет,
                // а не превращает весь материал в чёрное пятно.
                ambient *= painterlyAO;

                // На теневой стороне ambient сильнее,
                // на освещённой слабее.
                half ambientAmount = lerp(
                    0.26h,
                    0.10h,
                    lightBand
                );

                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));

                // Кромка сужена: pow по _RimPower давал широкий ореол по всему
                // силуэту. У эталона кромка тонкая и живёт на СВЕТЛОЙ стороне —
                // она подчёркивает форму, а не обводит фигуру целиком.
                half rimRaw = saturate(1.0h - dot(normal, viewDir));
                half rim = pow(rimRaw, _RimPower + 2.0h);
                rim *= 0.25h + 0.75h * lightBand;

                half3 color = texel.rgb * (tone + ambient * ambientAmount);

                // Дополнительный мягкий contact AO.
                // Не делает щели чёрными, только "сажает" объекты в сцену.
                half contactAO = lerp(
                    0.70h,
                    1.0h,
                    ssao
                );

                color *= contactAO;
                // Final stylized realtime cast shadow.
                // Не даём ambient полностью вымывать тень от объектов.
                // URP Shadow Strength уже ослабляет shadowAttenuation,
                // поэтому вытаскиваем из него более выразительную painterly-маску.
                

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

                // Угли кладутся ПОСЛЕ вспышки попадания и до тумана. После —
                // потому что вспышка это lerp в белое: положи кромку раньше, и
                // добивающий удар её съест ровно в тот кадр, когда начинается
                // растворение. До тумана — потому что дальние трупы должны
                // тухнуть вместе со сценой, а не гореть сквозь дымку.
                color += RazlomDissolveEmber() * dissolveFront;
                // ===== FINAL CAST SHADOW =====

                half castShadowMask =
                        saturate(1.0h - mainLight.shadowAttenuation);

                    color *= lerp(
                        1.0h,
                        0.50h,
                        castShadowMask
                    );

                color = MixFog(color, input.fogFactor);
                return half4(color, texel.a);
            }
            ENDHLSL
        }

        // КОНТАКТНОЕ СВЕЧЕНИЕ ВРАГА — три мягкие оболочки у основания.
        // Раздутая оболочка на всей высоте тела выглядела как неоновый halo;
        // RazlomGlowFrag отсекает верхнюю часть меша по GroundGlowBand.
        //
        // ПОРЯДОК В ФАЙЛЕ ЕСТЬ ПОРЯДОК ОТРИСОВКИ. Unity рисует подряд ВСЕ
        // проходы, у которых LightMode совпал с запрошенным тегом, сверху вниз.
        // Широкая и слабая обязана лечь первой, плотная — последней, иначе
        // затухание перевернётся и ореол получит жёсткую внешнюю кромку.
        // По той же причине настоящая обводка стоит после всех трёх: она
        // рисует плотное ядро контура поверх собранного свечения.
        //
        // Почему их три, а не одна широкая: одна даёт полосу постоянной
        // плотности, то есть рамку. Затухание берётся только из наложения.
        Pass
        {
            Name "GlowShellWide"
            Tags { "LightMode"="UniversalForward" }
            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RazlomDissolve.hlsl"
            #include "RazlomGlowShell.hlsl"

            GlowVaryings Vert(GlowAttributes input) { return RazlomGlowVert(input, 3.2, 0.72); }
            half4 Frag(GlowVaryings input) : SV_Target { return RazlomGlowFrag(input, 0.040h); }
            ENDHLSL
        }

        Pass
        {
            Name "GlowShellMid"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RazlomDissolve.hlsl"
            #include "RazlomGlowShell.hlsl"

            GlowVaryings Vert(GlowAttributes input) { return RazlomGlowVert(input, 1.8, 0.72); }
            half4 Frag(GlowVaryings input) : SV_Target { return RazlomGlowFrag(input, 0.070h); }
            ENDHLSL
        }

        Pass
        {
            Name "GlowShellNear"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RazlomDissolve.hlsl"
            #include "RazlomGlowShell.hlsl"

            GlowVaryings Vert(GlowAttributes input) { return RazlomGlowVert(input, 1.0, 0.72); }
            half4 Frag(GlowVaryings input) : SV_Target { return RazlomGlowFrag(input, 0.12h); }
            ENDHLSL
        }

        Pass
        {
            Name "UnitOutlineMask"
            Tags { "LightMode"="UnitOutlineMask" }
            Cull Back
            ZTest LEqual
            ZWrite Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RazlomDissolve.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };
            Varyings MaskVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }
            half4 MaskFrag(Varyings input) : SV_Target
            {
                clip(_OutlineWidth - 0.001h);
                RazlomDissolveFront(input.positionOS);
                // Premultiplied RGB сохраняет цвет на сглаженном краю MSAA.
                half width = saturate(_OutlineWidth / 3.0h);
                return half4(_OutlineColor.rgb * width, width);
            }
            ENDHLSL
        }

        Pass
        {
            Name "InkOutline"
            Tags { "LightMode"="InkOutline" }
            Cull Front
            // The feature runs after opaque bodies. Keep the silhouette
            // depth tested against the body, but never let its expanded
            // shell become the scene depth for subsequent effects.
            ZWrite Off
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "RazlomDissolve.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                // Сюда импорт кладёт нормаль, сшитую по положению вершины.
                // См. RazlomCharacterImport.OnPostprocessMesh.
                float4 tangentOS : TANGENT;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // Не раздутая оболочка, а исходная вершина: маска обязана
                // совпасть с той, что считает основной проход, иначе обводка
                // растворялась бы на пару пикселей позже тела.
                float3 positionOS : TEXCOORD0;
            };

            Varyings OutlineVert(Attributes input)
            {
                Varyings output;
                output.positionOS = input.positionOS.xyz;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // РАСТЯГИВАЕМ ПО СШИТОЙ НОРМАЛИ, А НЕ ПО ОБЫЧНОЙ. На жёстких
                // рёбрах и швах развёртки обычные нормали расходятся, оболочка
                // разрывается, и обводка проступает внутри силуэта. Сшитая
                // нормаль лежит в тангенсах; если её там нет — например у
                // модели, импортированной мимо нашего постпроцессора, — честно
                // падаем на обычную, и хуже, чем было, не станет.
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // Оболочка раздувается ТОЛЬКО ПО ЭКРАНУ (ниже), глубину вершины
                // проход не трогает — значит задние грани лежат там же, где и
                // были. У замкнутого меша они всегда за передними, и наружу
                // выходит ровно силуэт. У мобов же меш собран из кусков, которые
                // входят друг в друга: задняя грань лапы оказывается ближе
                // камеры, чем передняя грань юбки, проходит ZTest и рисует
                // контур ПОСРЕДИ тела. Это и есть «обводка захватывает
                // внутренние детали»; сшитая нормаль лечила другую болезнь —
                // разрывы на швах — и против этой бессильна.
                //
                // Отодвигаем вершину от камеры на _OutlineDepthBias метров.
                // Внутренний прорыв опережает закрывающую поверхность на
                // считанные сантиметры и после отступа проигрывает ей ZTest.
                float3 toCameraWS = GetWorldSpaceNormalizeViewDir(positionWS);
                positionWS -= toCameraWS * _OutlineDepthBias;

                output.positionCS = TransformWorldToHClip(positionWS);

                // Expand only the screen-space silhouette. Width zero is the
                // normal character state; gameplay enables this pass through
                // a MaterialPropertyBlock only for hostile targets.
                float3 normalVS = TransformWorldToViewDir(normalWS, true);
                float2 direction = normalVS.xy;
                float directionLength = length(direction);
                float silhouette = saturate(directionLength);
                float2 pixelSize = 2.0 / _ScreenParams.xy;
                output.positionCS.xy += (direction / max(directionLength, 0.0001)) *
                                        silhouette * pixelSize *
                                        _OutlineWidth * 1.6 * output.positionCS.w;

                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                // With no extrusion the back-face shell can still leak through
                // hard edges. Discard it completely instead of drawing a dark
                // zero-width contour on ordinary characters.
                clip(_OutlineWidth - 0.001h);
                half front = RazlomDissolveFront(input.positionOS);
                // Обводка на фронте уходит в угли. Оставь её тёмной — и
                // свечение основного прохода получит по контуру чёрную рамку
                // шириной в пиксель, ровно там, где оно должно быть ярче всего.
                return half4(lerp(_OutlineColor.rgb, RazlomDissolveEmber(), front),
                             _OutlineColor.a);
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
            #include "RazlomDissolve.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionOS = input.positionOS.xyz;

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
                // Свечение кромки тени не нужно, а вот clip внутри — нужен:
                // без него растворяющееся тело продолжает отбрасывать целую
                // тень, и на земле остаётся силуэт того, чего уже нет.
                RazlomDissolveFront(input.positionOS);
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
            #include "RazlomDissolve.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Как и в ShadowCaster: нужен только clip. Глубина обязана
                // совпадать с цветовым проходом до пикселя, иначе SSAO и
                // depth-эффекты видят тело там, где его уже выкусили.
                RazlomDissolveFront(input.positionOS);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
