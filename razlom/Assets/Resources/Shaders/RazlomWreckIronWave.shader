Shader "Razlom/Wreck Iron Wave"
{
    // ПОЛУМЕСЯЦ МАХОВ КРУШЕНИЯ v4 — «холодное железо» по целевым кадрам 06.10
    // (ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-swing-left.png,
    // v4-swing-right.png). V4 (06.10 вечер): владелец отверг мягкую версию —
    // «мыло… и линейно всё ещё очень». Теперь ЧЁТКО, как принятые волны сабли
    // («Razlom/Sabre Foam Wave»): плотное тело, полосы с жёстким порогом, тонкий
    // тёмный обвод постоянной ширины в пикселях вокруг ВСЕЙ формы, без дымки и
    // без полупрозрачности. Нелинейность — рисованная кисть:
    //  • внутренний край рвётся на штрихи кисти разной длины (своя маска мазков
    //    _BrushTex.r — дорожки вдоль маха), у острых концов полумесяца штрихи
    //    расходятся сильнее (кисть «распушается»);
    //  • границы полос (железо → ядро → лёд → белая кромка) идут по тем же
    //    мазкам, а не ровными линиями;
    //  • светлые штрихи-блики (_BrushTex.b) — отдельные заострённые мазки
    //    разной длины, а не синус;
    //  • толщина полосы по маху (сужение, утолщения) задана мешем
    //    (PelagWreckSwingIronVfxSetup.WaveMesh).
    // Меш: u вдоль маха (0 — короткая сторона, 1 — длинный острый конец),
    // v поперёк (0 — внутренний край, 1 — наружный). Возраст частицы — поток
    // AgePercent в TEXCOORD0.z: голова раскрывается, потом хвост рассыпается
    // по мазкам (жёсткий край с обводом), в самом конце — короткое гашение.
    Properties
    {
        _Deep ("Inner iron", Color) = (.12,.27,.74,1)
        _Water ("Core", Color) = (.22,.52,.98,1)
        _Shallow ("Light core", Color) = (.56,.86,1,1)
        _Foam ("Rim", Color) = (1.12,1.18,1.24,1)
        _FoamShade ("Rim shade", Color) = (.80,.93,1.02,1)
        _Outline ("Outline", Color) = (.05,.10,.30,1)
        _EdgeBlue ("Edge blue (inside the outline)", Color) = (.16,.38,.80,1)
        _EdgeBluePx ("Edge blue width, px", Range(0,6)) = 2.2
        _BrushTex ("Brush (r lanes, g coarse, b highlight strokes)", 2D) = "gray" {}
        _BrushScale ("Brush scale (lanes u, lanes v, coarse u, coarse v)", Vector) = (1.3,1,1.6,.6)
        _Bands ("Bands (deep end, core end, rim from, band rag)", Vector) = (.36,.62,.82,.10)
        _Inner ("Inner edge (base v, rag, end fray, end share)", Vector) = (.16,.26,.40,.22)
        _EdgeRag ("Outer edge rag", Range(0,.5)) = .03
        _HeadFoam ("Extra rim at the head", Range(0,.5)) = .06
        _OutlinePx ("Outline width, px", Range(0,6)) = 2.2
        _Aspect ("Length / width", Float) = 8
        _ErodeGain ("Erosion edge in width units", Float) = 1.7
        _Streaks ("Highlight strokes", Range(0,1)) = .8
        _Glow ("Rim glow", Range(0,1)) = .12
        _Opacity ("Opacity", Range(0,1)) = 1
        _LifeSeconds ("Particle lifetime (s)", Float) = .3
        _HeadFrom ("Head start fraction", Float) = .55
        _HeadSeconds ("Head seconds", Float) = .06
        _ErodeFrom ("Erode from (s)", Float) = .1
        _ErodeTo ("Erode to (s)", Float) = .26
        _ErodeAlong ("Erosion follows u (tail first)", Range(0,1)) = .7
        _FadeFrom ("Fade from (s)", Float) = .24
        _FadeTo ("Fade to (s)", Float) = .28
        _FlowSpeed ("Flow speed (u/s)", Float) = .6
        _CameraPush ("Push toward camera (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+40" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Iron Wave"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Deep, _Water, _Shallow, _Foam, _FoamShade, _Outline, _EdgeBlue;
            float4 _BrushTex_ST, _BrushScale, _Bands, _Inner;
            float _CameraPush, _EdgeRag, _HeadFoam, _OutlinePx, _EdgeBluePx, _Aspect, _ErodeGain, _Streaks, _Glow, _Opacity;
            float _LifeSeconds, _HeadFrom, _HeadSeconds, _ErodeFrom, _ErodeTo, _ErodeAlong, _FadeFrom, _FadeTo, _FlowSpeed;
            CBUFFER_END
            TEXTURE2D(_BrushTex); SAMPLER(sampler_BrushTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                positionWS += GetWorldSpaceNormalizeViewDir(positionWS) * _CameraPush;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv.xyz;
                o.color = v.color;
                return o;
            }

            // Жёсткий порог с шириной сглаживания в пиксель: 0 → 1 при переходе x через edge.
            float Step(float edge, float x)
            {
                float aa = max(fwidth(x) * .75, 1e-4);
                return saturate((x - edge) / aa + .5);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float age = saturate(i.uv.z) * _LifeSeconds;
                float headT = 1 - pow(1 - saturate(age / max(.01, _HeadSeconds)), 2.2);
                float headP = lerp(_HeadFrom, 1.02, headT);
                float erodeP = 1.05 * smoothstep(_ErodeFrom, max(_ErodeFrom + .01, _ErodeTo), age);
                float fade = _Opacity * (1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), age));
                float flow = age * _FlowSpeed;

                // Мазки кисти: r — дорожки вдоль маха (у каждой своя глубина), b — заострённые блики.
                half4 brush = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex,
                    float2((u - flow * .25) * _BrushScale.x, v * _BrushScale.y));
                float lane = brush.r, glint = brush.b;
                float coarse = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex,
                    float2((u - flow * .5) * _BrushScale.z + .37, v * _BrushScale.w + .21)).g;

                // Острые концы полумесяца «распушаются»: штрихи расходятся глубже.
                float endT = 1 - smoothstep(0, max(.01, _Inner.w), min(u, 1 - u));
                float rag = _Inner.y + _Inner.z * endT;

                // Силуэт (+ внутри, в долях ширины полосы): наружный край — почти ровная дуга, внутренний — мазки,
                // голова срезана по раскрытию, хвост рассыпается по мазкам и крупному шуму.
                float dOuter = (1 - _EdgeRag * (1 - coarse)) - v - endT * (1 - lane) * .18;
                float dInner = (v - _Inner.x) - (lane - .5) * rag * 2;
                float dHead = (headP - u) * _Aspect;
                float field = lerp(coarse, u * .62 + coarse * .38, _ErodeAlong) + (lane - .5) * .22;
                float dErode = (field - erodeP) * _ErodeGain;
                // Край меша (v = 0 и концы по u) — тоже с обводом, а не срезом пикселей.
                float dMesh = min(v - .004, min(u, 1 - u) * _Aspect);
                float d = min(min(min(dOuter, dInner), min(dHead, dErode)), dMesh);
                // Обвод постоянной ширины в пикселях: расстояние делится на его производную.
                float px = d / max(fwidth(d), 1e-5);
                float shape = saturate(px + .5);
                float body = saturate(px - _OutlinePx + .5);

                // Полосы поперёк, границы — по мазкам (тот же r): рисованные, не ровные.
                float vb = v + (lane - .5) * _Bands.w;
                half3 col = _Deep.rgb;
                col = lerp(col, _Water.rgb, Step(_Bands.x, vb));
                float coreT = Step(_Bands.y, vb);
                col = lerp(col, _Shallow.rgb, coreT);

                // Белая кромка: у головы шире (гребень), граница чуть рвётся мазками.
                float crest = smoothstep(.55, .9, u / max(headP, .01)) * (1 - smoothstep(.9, 1, u));
                float rimFrom = _Bands.z - crest * _HeadFoam - (lane - .5) * .05;
                float rim = Step(rimFrom, v);
                // Блики — отдельные заострённые мазки в ядре и железе (не в кромке).
                float glints = Step(.5, glint) * (1 - rim) * Step(_Bands.x - .08, vb);
                col = lerp(col, lerp(_Shallow.rgb, _Foam.rgb, .6), glints * _Streaks);
                half3 rimCol = lerp(_FoamShade.rgb, _Foam.rgb, Step(.45, coarse));
                col = lerp(col, rimCol, rim);

                // Край в два тона, как на кадре: тёмно-синий обвод снаружи, под ним — полоска насыщенного синего.
                float inner = saturate(px - _OutlinePx - _EdgeBluePx + .5);
                col = lerp(_EdgeBlue.rgb, col, inner);
                col = lerp(_Outline.rgb, col, body);
                float alpha = shape * lerp(_Outline.a, 1, body) * fade * i.color.a;
                // Свечение — только внутри белой кромки (без ореола вокруг формы).
                half3 glow = rimCol * rim * inner * shape * fade * _Glow;
                half3 rgb = (col * alpha + glow) * i.color.rgb;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
