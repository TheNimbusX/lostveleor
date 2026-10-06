Shader "Razlom/Whirlwind Form Water"
{
    // ВОДА ФОРМ ВИХРЯ (владелец 02.10, вечер): кольца Пенных волн и рукава
    // Водоворота. «Слишком линейные волны… застывает… надо более водянистое»,
    // «анимация не плавная, резаная, линейная» — прежние формы были мешевыми
    // частицами на шейдере волны сабли: идеальная полоса, распад дырами с
    // тёмным обводом («чёрное кружево»).
    //
    // Язык — пенный след рывка (Razlom/Dash Foam Wake, кадр А, принят 02.10):
    // плоская вода, комковатые валики пены по краям, тонкий тёмный обвод
    // постоянной ширины в пикселях, распад на круглые капли с голубой тенью
    // (без тёмных колец). Отличия от следа рывка:
    //  • шум берётся по двумерной координате «материала» из вершин (у кольца
    //    — окружность в пространстве шума, без шва; у рукава — вдоль и
    //    поперёк рукава), а не по метрам вдоль прямой полосы;
    //  • гребни разные: внешний (y > 0 — бегущий фронт кольца, выпуклый край
    //    рукава) толще, внутренний тоньше и с тёмной глубиной у кромки;
    //  • концы — по расстоянию до открытого конца из вершин (у кольца концов нет).
    //
    // v4 (проверка по выбранным кадрам waves-2 / vortex-1):
    //  • поперёк — градиент глубины (_Across.x): у внутреннего края глубокая
    //    вода, к внешнему светлеет; внутренняя сторона рукава может быть
    //    прозрачнее и без обвода (_Across.y/z), внешний гребень — рваный
    //    (_Across.w): крупные комья пены с просветами воды между ними;
    //  • вода не пропадает под низкими препятствиями (колодец, корни, кочки,
    //    трава): тест глубины свой. Тела врагов и героя (Razlom/Texture Toon)
    //    пишут в трафарет 1 — по ним вода не рисуется никогда, как и раньше
    //    тело закрывало воду. Всё остальное, что ближе к камере, сравнивается
    //    по _CameraDepthTexture: если видимая поверхность выше воды не больше
    //    чем на _Over.x м (с мягким краем _Over.y), вода ложится поверх неё,
    //    как на кадре вода перекатывается через корень, и там белеет пеной
    //    (_Over.z). Выше — ствол, стена — вода за ним, как была.
    //    COLOR.r — сила этого правила у вершины (у ног героя ноль: сабля не
    //    пишет трафарет), там действует прежний допуск _Over.w.
    //
    // Меш пересобирает вид каждый кадр по непрерывному времени
    // (PelagWhirlwindFormWater.cs), в кадр шейдеру ничего не пишется:
    //   TEXCOORD0.x — поперёк в долях полуширины воды: ±1 — край воды, + наружу;
    //   TEXCOORD0.y — полуширина воды здесь, м;
    //   TEXCOORD0.z — возраст для распада, с (вид сдвигает его по месту);
    //   TEXCOORD0.w — до ближайшего открытого конца, м (у кольца — далеко);
    //   TEXCOORD1.xy — координата шума, м;
    //   TEXCOORD1.z — сдвиг шума на эффект;
    //   TEXCOORD1.w — лишняя пена гребня, доля полуширины (бурление фронта);
    //   COLOR.r — вода ложится поверх низких препятствий (0…1);
    //   COLOR.a — непрозрачность.
    // Смешивание премультиплированное, как у волны сабли и следа рывка.
    Properties
    {
        _Deep ("Deep water (inner edge, dark streaks)", Color) = (.03,.24,.32,1)
        _Water ("Water", Color) = (.06,.60,.68,1)
        _Shallow ("Shallow water", Color) = (.42,.90,.92,1)
        _Foam ("Foam", Color) = (1.12,1.22,1.22,1)
        _FoamShade ("Foam shade (between clumps)", Color) = (.62,.86,.91,1)
        _Outline ("Outline", Color) = (.02,.08,.11,.92)
        _FoamTex ("Foam noise (r), bubble cells", 2D) = "gray" {}
        _ErodeTex ("Soft noise (r), round blobs", 2D) = "gray" {}
        _CoarseScale ("Coarse noise tiles per metre", Float) = .5
        _ClumpScale ("Foam clump cells tiles per metre", Float) = .85
        _Bands ("Across: light water from, to; deep inner edge share, deep strength", Vector) = (.15,.75,.45,.55)
        _Across ("Across: deep-to-light gradient, inner alpha, inner outline, broken outer crest", Vector) = (0,1,1,0)
        _Crest ("Crest: depth m, bulge m, edge rag m, inner crest share", Vector) = (.12,.09,.03,.45)
        _EndRag ("Open end rag depth, m", Float) = .28
        _Lines ("Flow lines: count across, width px, dash tiles per metre, strength", Vector) = (4.5,1.6,.40,.85)
        _DarkLines ("Depth lines: count across, width px, dash tiles per metre, strength", Vector) = (3,2.6,.32,.45)
        _OutlinePx ("Outline width, px", Range(0,6)) = 2
        _Glow ("Foam glow", Range(0,1)) = .2
        _Break ("Break: cracks at age s, rag s, -, foam-up s", Vector) = (.30,.06,0,.10)
        _BreakScale ("Droplet blobs tiles per metre", Float) = 2.2
        _DropLife ("Droplets outlive the cracks by, s", Float) = .07
        _EdgeEarly ("Crests break earlier by, s per |y|", Float) = .03
        _BreakRimPx ("Shade rim of the break edges, px", Range(0,4)) = 1.5
        _FadeFrom ("Safety fade from (age, s)", Float) = .40
        _FadeTo ("Safety fade to (age, s)", Float) = .46
        _Over ("Over low obstacles: max rise m, soft m, foam over them, tolerance m", Vector) = (1,.2,.5,.3)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+36" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Whirlwind Form Water"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            // Тела (Razlom/Texture Toon, проход ForwardToon) пишут 1: поверх тел вода не ложится.
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _Deep, _Water, _Shallow, _Foam, _FoamShade, _Outline;
            float4 _FoamTex_ST, _ErodeTex_ST, _Bands, _Across, _Crest, _Lines, _DarkLines, _Break, _Over;
            float _CoarseScale, _ClumpScale, _EndRag, _OutlinePx, _Glow;
            float _BreakScale, _DropLife, _EdgeEarly, _BreakRimPx, _FadeFrom, _FadeTo;
            CBUFFER_END
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_ErodeTex); SAMPLER(sampler_ErodeTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv0 = v.uv0;
                o.uv1 = v.uv1;
                o.color = v.color;
                return o;
            }

            float Bubbles(float2 p) { return SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, p).r; }
            float Soft(float2 p) { return SAMPLE_TEXTURE2D(_ErodeTex, sampler_ErodeTex, p).r; }

            // Струи вдоль воды: n полос поперёк (по доле ширины), ширина постоянная
            // в пикселях, каждая горит отрезками по мягкому шуму в координате воды.
            float FlowLines(float y, float2 n, float wobble, float4 p, float shift, float seed)
            {
                float lw = (y * .5 + .5) * p.x + wobble + shift;
                float idx = floor(lw);
                float px = abs(frac(lw) - .5) / max(fwidth(lw), 1e-5);
                float stripe = 1 - smoothstep(p.y * .5 - .5, p.y * .5 + .5, px);
                float gate = Soft(n * p.z + float2(idx * .37 + seed * .13 + shift, idx * .29 + .61 + seed * .07));
                return stripe * smoothstep(.47, .60, gate);
            }

            // Насколько видимая в этом пикселе поверхность (без тел) выше самой воды, м.
            float SceneRise(float4 positionCS, float waterY)
            {
                float2 uv = GetNormalizedScreenSpaceUV(positionCS);
                float raw = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 scene = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                return scene.y - waterY;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float y = i.uv0.x, hw = max(i.uv0.y, .01), age = max(0, i.uv0.z), endD = i.uv0.w;
                float2 n = i.uv1.xy;
                float seed = i.uv1.z, churn = i.uv1.w;
                float ay = abs(y);
                float outerSide = step(0, y);
                // Метры внутрь от края воды (за краем — меньше нуля).
                float inside = (1 - ay) * hw;

                float coarse = Bubbles(n * _CoarseScale + float2(.37 + seed * .5, .21 + seed));
                float coarse2 = Bubbles(n * _CoarseScale * .8 + float2(.71 + seed, .53 + seed * .4));
                // Комья пены: ячейки пузырькового шума, середины к единице, швы — ноль.
                float clump = saturate(Bubbles(n * _ClumpScale + float2(seed * .7, .5 + seed * .29)) * 2);
                // Рваный внешний гребень (_Across.w): вдоль кромки куски пены сменяются просветами воды.
                float gate = smoothstep(.36, .60, Soft(n * .55 + float2(.19 + seed * .31, .43 + seed * .17)));
                float crestKeep = lerp(1, .18 + .82 * gate, _Across.w * outerSide);

                // ---- гребни: внешний — валик пены и бурление фронта, внутренний тоньше.
                float side = lerp(_Crest.w, 1, outerSide);
                float crestDepth = _Crest.x * side * (.7 + .6 * coarse) * crestKeep;
                float foamDepth = crestDepth * (.5 + clump) + churn * lerp(.25, 1, outerSide) * hw * (.55 + .9 * clump) * crestKeep;
                float fd = foamDepth - inside;
                float foam = saturate(fd / max(fwidth(fd), 1e-5) + .5);

                // ---- силуэт в метрах: комья выпирают за край, концы рваные.
                float bulge = _Crest.y * side * smoothstep(.2, .65, clump) * crestKeep;
                float dSide = inside + bulge - _Crest.z * (1 - coarse);
                float dEnd = endD - _EndRag * coarse2;
                float d = min(dSide, dEnd);
                float px = d / max(fwidth(d), 1e-5);
                float shape = saturate(px + .5);
                float body = saturate(px - _OutlinePx + .5);
                // Обвод внутренней кромки (_Across.z): у рукава она уходит в воронку мягко, без туши.
                body = lerp(1, body, lerp(_Across.z, 1, outerSide));

                // ---- вода: плоская, к краям светлеет; у внутренней кромки — глубина.
                float vb = saturate(ay + (coarse - .5) * .30);
                half3 col = lerp(_Water.rgb, _Shallow.rgb, saturate(smoothstep(_Bands.x, _Bands.y, vb) * .7));
                float innerDeep = (1 - outerSide) * smoothstep(1 - _Bands.z, 1, ay + (coarse2 - .5) * .2);
                col = lerp(col, _Deep.rgb, innerDeep * _Bands.w);
                // v4 (_Across.x): поперёк — от глубокой воды у внутреннего края к светлой у внешнего.
                float s = saturate(y * .5 + .5 + (coarse - .5) * .18);
                half3 grad = lerp(_Deep.rgb, _Water.rgb, smoothstep(0, .50, s));
                grad = lerp(grad, _Shallow.rgb, smoothstep(.50, 1, s));
                col = lerp(col, grad, _Across.x);

                // ---- струи: светлые и тёмные, чуть волнистые.
                float wobble = (coarse - .5) * .35;
                float light = FlowLines(y, n, wobble, _Lines, 0, seed) * _Lines.w;
                float dark = FlowLines(y, n, wobble * .8, _DarkLines, .5, seed + 3.1) * _DarkLines.w;
                col = lerp(col, lerp(_Water.rgb, _Deep.rgb, .55), dark);
                col = lerp(col, lerp(_Shallow.rgb, _Foam.rgb, .7), light);

                // ---- низкие препятствия: вода поверх всего, что не выше _Over.x над ней (тела — трафаретом).
                float rise = SceneRise(i.positionCS, i.positionWS.y);
                float over = saturate(i.color.r);
                float cap = lerp(_Over.w, _Over.x, over);
                float covered = 1 - smoothstep(cap, cap + max(_Over.y, .01), rise);
                // Через корень или камень вода перекатывается — там она белеет комьями пены.
                float wash = over * smoothstep(.06, .30, rise) * _Over.z;
                foam = max(foam, smoothstep(.40, .55, wash * (.35 + clump)));

                // ---- распад: к возрасту _Break.x (± _Break.y/2 по мягкому шуму)
                // вода сначала белеет пеной (_Break.w), потом пена рвётся на
                // круглые капли, их середины живут ещё _DropLife. Гребни уходят
                // чуть раньше середины. Дыр внутри живой воды нет.
                float brk = Soft(n * 1.1 + float2(.13 + seed * .37, .77 + seed * .11));
                float cell = Soft(n * _BreakScale + float2(.31 + seed * .23, .17 + seed * .41));
                float drop = saturate((cell - .30) / .45);
                float older = age + saturate(ay) * _EdgeEarly;
                float crack = _Break.x + (brk - .5) * _Break.y - older;
                float life = crack + _DropLife * drop;
                float lifePx = life / max(fwidth(life), 1e-6);
                float keep = saturate(lifePx + .5);
                float foamUp = saturate(1 - crack / max(_Break.w, 1e-3));
                foam = max(foam, smoothstep(.2, .8, foamUp + (drop - .5) * .3));

                half3 foamCol = lerp(_FoamShade.rgb, _Foam.rgb, smoothstep(.12, .42, lerp(clump, drop, foamUp)));
                col = lerp(col, foamCol, foam);
                // Края капель — тень пены, не тёмный обвод.
                float rim = (1 - saturate(lifePx - _BreakRimPx + .5)) * saturate(foamUp * 2 - 1);
                col = lerp(col, _FoamShade.rgb * .92, rim * .85);

                col = lerp(_Outline.rgb, col, body);
                float fade = 1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), age);
                // Внутренняя сторона прозрачнее (_Across.y): вода уходит в глубину воронки; пена — всегда плотная.
                float depthAlpha = max(lerp(_Across.y, 1, smoothstep(0, .55, s)), foam);
                float alpha = shape * lerp(_Outline.a, 1, body) * keep * fade * covered * depthAlpha * i.color.a;
                half3 glow = foamCol * foam * body * alpha * _Glow;
                return half4(col * alpha + glow, alpha);
            }
            ENDHLSL
        }
    }
}
