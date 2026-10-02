Shader "Razlom/Dash Foam Wake"
{
    // ПЕННЫЙ СЛЕД РЫВКА ПЕЛАГА — выбор владельца 02.10: кадр А «Пенный след»
    // (ART/characters/pelag/dash-2026-10-02/1-A-foam-wake.png) со всплеском у
    // передней ноги из А5. Семья «морской пены» серии сабли: те же цвета,
    // тот же пузырьковый шум пака CFXR («cfxr sword trail noise bubbles»),
    // тот же обвод постоянной ширины в пикселях, что у Razlom/Sabre Foam Wave.
    //
    // Отдельный шейдер, а не материал волны сабли, потому что у следа пена по
    // ОБОИМ краям, а у волны — только по внешнему (внутренний край волны
    // ровный, с обводом), и шум волны идёт по долям меша, а не по метрам:
    // при росте следа вместе с героем рисунок растягивался бы.
    //
    // Меш — плоская полоса на земле, её каждый кадр пересобирает
    // PelagDashWake (Game.View/PelagVfxController.Dash.cs) по НАСТОЯЩЕМУ пути
    // героя: от точки старта до ног. В кадр шейдеру ничего не пишется — всё
    // в вершинах:
    //   TEXCOORD0.x — s, метры вдоль рывка от точки старта (позади неё — меньше 0);
    //   TEXCOORD0.y — поперёк в долях полуширины ВОДЫ: ±1 — край воды; меш шире
    //                 на поле PelagDashWake.Margin, туда выпирают комья пены;
    //   TEXCOORD0.z — возраст участка, с: сколько прошло, как герой его пробежал;
    //   TEXCOORD0.w — s у ног, где след кончается;
    //   TEXCOORD1.x — полуширина воды в этом месте, м;
    //   TEXCOORD1.y — сдвиг шума на рывок (каждый след свой);
    //   TEXCOORD1.z — сила свечения пены (настройка «Вспышки»);
    //   TEXCOORD1.w — s у хвоста, где начинается меш.
    //
    // v2 (проверка по кадрам А/А5, 02.10):
    //  • внутри — не россыпь белых овалов, а несколько длинных струй вдоль
    //    рывка (светлые и тёмные), как на А;
    //  • гребни по обоим краям — сплошные комковатые валики пены постоянной
    //    толщины в метрах (у хвоста не истончаются в нитку), комья выпирают за
    //    край воды и обведены, к ногам пена гуще и сходится в кашу;
    //  • распад — фронтом от старта к ногам: перед фронтом вода белеет пеной,
    //    на фронте пена рвётся на капли и они тают. Края распада НЕ обводятся
    //    тёмным (тёмные кольца дыр читались грязью на земле) — у них голубая
    //    тень пены, как у капель.
    //
    // Смешивание премультиплированное, как у волны сабли.
    Properties
    {
        _Deep ("Deep water (dark streaks)", Color) = (.03,.24,.32,1)
        _Water ("Water", Color) = (.06,.60,.68,1)
        _Shallow ("Shallow water", Color) = (.42,.90,.92,1)
        _Foam ("Foam", Color) = (1.12,1.22,1.22,1)
        _FoamShade ("Foam shade (between clumps)", Color) = (.62,.86,.91,1)
        _Outline ("Outline", Color) = (.02,.08,.11,.92)
        _FoamTex ("Foam noise (r), bubble cells", 2D) = "gray" {}
        _ErodeTex ("Soft noise (r), round blobs", 2D) = "gray" {}
        _CoarseScale ("Coarse noise tiles per metre (along, across)", Vector) = (.42,.65,0,0)
        _ClumpScale ("Foam clump cells tiles per metre", Float) = .85
        _Bands ("Across |y|: light water from, light water to", Vector) = (.15,.75,0,0)
        _Crest ("Crest: depth at tail m, depth at feet m, bulge m, edge rag m", Vector) = (.07,.16,.09,.03)
        _HeadFoam ("Foam at the feet, share of half width", Range(0,1.5)) = .6
        _CrestLength ("Foam churn length at the feet, m", Float) = .9
        _Lines ("Flow lines: count across, width px, dash tiles per metre, strength", Vector) = (4.5,1.6,.40,.85)
        _DarkLines ("Depth lines: count across, width px, dash tiles per metre, strength", Vector) = (3,2.6,.32,.45)
        _TailRag ("Tail rag depth, m", Float) = .32
        _HeadRag ("Head rag depth, m", Float) = .30
        _OutlinePx ("Outline width, px", Range(0,6)) = 2
        _Glow ("Foam glow", Range(0,1)) = .25
        _Break ("Break: cracks at segment age s, rag s, tail lead s, foam-up s", Vector) = (.30,.06,.11,.04)
        _BreakScale ("Droplet blobs tiles per metre", Float) = 2.2
        _DropLife ("Droplets outlive the cracks by, s", Float) = .07
        _EdgeEarly ("Crests break earlier by, s per |y|", Float) = .03
        _BreakRimPx ("Shade rim of the break edges, px", Range(0,4)) = 1.5
        _FadeFrom ("Safety fade from (segment age, s)", Float) = .37
        _FadeTo ("Safety fade to (segment age, s)", Float) = .41
        _FlowSpeed ("Flow lines drift back, m/s", Float) = .8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+38" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Dash Foam Wake"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Deep, _Water, _Shallow, _Foam, _FoamShade, _Outline;
            float4 _FoamTex_ST, _ErodeTex_ST, _CoarseScale, _Bands, _Crest, _Lines, _DarkLines, _Break;
            float _ClumpScale, _HeadFoam, _CrestLength, _TailRag, _HeadRag, _OutlinePx, _Glow;
            float _BreakScale, _DropLife, _EdgeEarly, _BreakRimPx, _FadeFrom, _FadeTo, _FlowSpeed;
            CBUFFER_END
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_ErodeTex); SAMPLER(sampler_ErodeTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv0 = v.uv0;
                o.uv1 = v.uv1;
                o.color = v.color;
                return o;
            }

            float Bubbles(float2 p) { return SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, p).r; }
            float Soft(float2 p) { return SAMPLE_TEXTURE2D(_ErodeTex, sampler_ErodeTex, p).r; }

            // Струи вдоль рывка: n полос поперёк (по доле ширины — сходятся к
            // хвосту вместе со следом), ширина постоянная в пикселях, каждая
            // горит длинными отрезками по своему мягкому шуму.
            float FlowLines(float y, float s, float wobble, float4 p, float shift, float seed)
            {
                float lw = (y * .5 + .5) * p.x + wobble + shift;
                float idx = floor(lw);
                float px = abs(frac(lw) - .5) / max(fwidth(lw), 1e-5);
                float stripe = 1 - smoothstep(p.y * .5 - .5, p.y * .5 + .5, px);
                float gate = Soft(float2(s * p.z + idx * .37 + seed * .13 + shift, idx * .29 + .61 + seed * .07));
                return stripe * smoothstep(.47, .60, gate);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float s = i.uv0.x, y = i.uv0.y, age = max(0, i.uv0.z), headS = i.uv0.w;
                float hw = max(i.uv1.x, .01), seed = i.uv1.y, glowScale = i.uv1.z, tailS = i.uv1.w;
                float ay = abs(y);
                // Поперёк в метрах со знаком: левый и правый края берут разный
                // шум, гребни не зеркалят друг друга.
                float across = y * hw;
                // Метры внутрь от края воды (за краем — меньше нуля).
                float inside = (1 - ay) * hw;
                // 0 у хвоста … 1 у ног.
                float k = saturate((s - tailS) / max(headS - tailS, .01));

                float coarse = Bubbles(float2(s * _CoarseScale.x + .37 + seed * .5, across * _CoarseScale.y + .21 + seed));
                float coarse2 = Bubbles(float2(s * _CoarseScale.x * .8 + .71 + seed, across * _CoarseScale.y * 1.3 + .53 + seed * .4));
                // Комья пены: ячейки пузырькового шума, одинаковые вдоль и
                // поперёк в метрах — круглые «цветные капусты» по ~15 см.
                // Шум тёмный (медиана 0,2, светлее 0,5 — десятая часть), ×2 —
                // середины ячеек к единице, швы остаются нулём.
                float clump = saturate(Bubbles(float2(s * _ClumpScale + seed * .7, across * _ClumpScale + .5 + seed * .29)) * 2);

                // ---- гребни: сплошной комковатый валик пены вдоль обоих краёв
                float head = smoothstep(headS - _CrestLength, headS, s);
                float crestDepth = lerp(_Crest.x, _Crest.y, k) * (.7 + .6 * coarse);
                float foamDepth = crestDepth * (.5 + clump) + head * _HeadFoam * hw * (.55 + .9 * clump);
                float fd = foamDepth - inside;
                float foam = saturate(fd / max(fwidth(fd), 1e-5) + .5);

                // ---- силуэт в метрах: положительное расстояние внутри. Комья
                // выпирают за край воды, хвост — рваный срез шумом (не игла), у
                // ног — короткий рваный край.
                float bulge = _Crest.z * smoothstep(.2, .65, clump) * saturate(.55 + .45 * k + head);
                float dSide = inside + bulge - _Crest.w * (1 - coarse);
                float dTail = (s - tailS) - _TailRag * coarse2;
                float dHead = (headS - s) - _HeadRag * (1 - coarse);
                float d = min(dSide, min(dTail, dHead));
                // Обвод постоянной ширины в пикселях: расстояние делится на его производную.
                float px = d / max(fwidth(d), 1e-5);
                float shape = saturate(px + .5);
                float body = saturate(px - _OutlinePx + .5);

                // ---- вода: плоская бирюза, к краям мягко светлеет (кадр А).
                float vb = saturate(ay + (coarse - .5) * .30);
                half3 col = lerp(_Water.rgb, _Shallow.rgb, saturate(smoothstep(_Bands.x, _Bands.y, vb) * .7));

                // ---- струи вдоль рывка: светлые и тёмные (глубина), длинные,
                // чуть волнистые; рисунок участка с возрастом сползает к старту.
                float drift = age * _FlowSpeed;
                float wobble = (coarse - .5) * .35;
                float light = FlowLines(y, s + drift, wobble, _Lines, 0, seed) * _Lines.w;
                float dark = FlowLines(y, s + drift, wobble * .8, _DarkLines, .5, seed + 3.1) * _DarkLines.w;
                col = lerp(col, lerp(_Water.rgb, _Deep.rgb, .55), dark * (1 - head));
                col = lerp(col, lerp(_Shallow.rgb, _Foam.rgb, .7), light * (1 - head * .6));

                // ---- распад: фронт от старта к ногам. Участок трескается к
                // возрасту _Break.x (± _Break.y/2 по мягкому шуму); хвост старше
                // на _Break.z, гребни уходят чуть раньше оси. За _Break.w до
                // трещин вода белеет пеной; трещины идут по низинам мелкого
                // мягкого шума — пена рвётся на круглые капли, их середины
                // живут ещё _DropLife и тают к центру. Дыр внутри живой воды
                // нет: до фронта трещин не бывает, за ним — только капли.
                // (Швы пузырькового шума давали червеобразный лабиринт.)
                float brk = Soft(float2(s, across) * 1.1 + float2(.13 + seed * .37, .77 + seed * .11));
                float cell = Soft(float2(s, across) * _BreakScale + float2(.31 + seed * .23, .17 + seed * .41));
                float drop = saturate((cell - .30) / .45);
                float older = age + _Break.z * (1 - k) + saturate(ay) * _EdgeEarly;
                float crack = _Break.x + (brk - .5) * _Break.y - older;
                float life = crack + _DropLife * drop;
                float lifePx = life / max(fwidth(life), 1e-6);
                float keep = saturate(lifePx + .5);
                float foamUp = saturate(1 - crack / max(_Break.w, 1e-3));
                foam = max(foam, smoothstep(.2, .8, foamUp + (drop - .5) * .3));

                half3 foamCol = lerp(_FoamShade.rgb, _Foam.rgb, smoothstep(.12, .42, lerp(clump, drop, foamUp)));
                col = lerp(col, foamCol, foam);
                // Края капель — голубая тень пены, не тёмный обвод.
                float rim = (1 - saturate(lifePx - _BreakRimPx + .5)) * saturate(foamUp * 2 - 1);
                col = lerp(col, _FoamShade.rgb * .92, rim * .85);

                col = lerp(_Outline.rgb, col, body);
                float fade = 1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), age);
                float alpha = shape * lerp(_Outline.a, 1, body) * keep * fade * i.color.a;
                half3 glow = foamCol * foam * body * alpha * _Glow * glowScale;
                half3 rgb = (col * alpha + glow) * i.color.rgb;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
