Shader "Razlom/Wreck Combo Wave"
{
    // ВОЛНА МАХОВ КРУШЕНИЯ — «холодное железо» на стеке серии сабли (06.10 вечер, Editor/PelagWreckComboVfxSetup).
    //
    // Копия приёмов «Razlom/Sabre Foam Wave» (сам шейдер сабли не трогается): полумесяц пака CFXR
    // «sword_trail 180 thick», u вдоль маха (0 — хвост, откуда шёл якорь, 1 — голова за якорем), v поперёк
    // (0 — внутренний край к герою, 1 — внешний). Отличие — МАТЕРИАЛ, не техника:
    //  • полосы кобальта и стали: глубина у внутреннего края, кобальт, светлая сталь, у внешнего края — горячая
    //    почти белая холодная кромка (у сабли там пена);
    //  • шум — гранёные ячейки пака «cfxr sword trail noise ice», вытянутые вдоль маха: длинные плоские грани
    //    стали, жёсткие ступени вместо пузырьков; внешний край и распад рвутся углами (осколки), а не кругами;
    //  • жёсткие металлические штрихи вдоль маха (полосы-«протяжка», рвутся по граням) и блик, пробегающий
    //    от хвоста к голове, — движение внутри формы;
    //  • голова тупая и скруглённая (_HeadRound), тяжелее сабельной.
    // Раскадровка по возрасту частицы (поток AgePercent в TEXCOORD0.z): голова бежит за якорем, хвост
    // рассыпается гранями, форма гаснет. Обвод — постоянной ширины в пикселях, тёмно-синие чернила.
    // Смешивание премультиплированное, как у сабли.
    Properties
    {
        _Deep ("Deep (inner edge)", Color) = (.04,.12,.29,1)
        _Mid ("Cobalt", Color) = (.18,.44,.88,1)
        _Light ("Light steel", Color) = (.56,.77,1,1)
        _Hot ("Hot rim (near-white cold)", Color) = (1.10,1.18,1.30,1)
        _HotShade ("Hot rim facet shade", Color) = (.70,.84,1,1)
        _Outline ("Ink outline", Color) = (.02,.04,.12,.94)
        _FacetTex ("Facet noise (r)", 2D) = "gray" {}
        _FacetScale ("Facet scale (fine u, fine v, coarse u, coarse v)", Vector) = (1.4,2.2,.9,.8)
        _Bands ("Bands (deep end, cobalt end, hot from, hot rag)", Vector) = (.20,.46,.70,.16)
        _EdgeRag ("Outer edge rag", Range(0,.5)) = .14
        _EdgeTeeth ("Outer edge teeth depth", Range(0,.4)) = .12
        _TeethCount ("Teeth along the arc", Float) = 16
        _HeadHot ("Extra hot rim at the head", Range(0,.5)) = .16
        _HeadRound ("Blunt head roundness (u)", Range(0,.3)) = .10
        _OutlinePx ("Outline width, px", Range(0,6)) = 2.4
        _Aspect ("Length / width", Float) = 5
        _ErodeGain ("Erosion edge in width units", Float) = 1.6
        _Streaks ("Metal streaks", Range(0,1)) = .8
        _SliverScale ("Slivers (u scale, v scale, threshold, flow x)", Vector) = (.55,4.5,.80,1.6)
        _Sheen ("Travelling sheen", Range(0,1)) = .75
        _SheenWidth ("Sheen width (u)", Range(.01,.5)) = .10
        _SheenSeconds ("Sheen tail-to-head seconds", Float) = .16
        _Glow ("Hot rim glow", Range(0,1)) = .30
        _Opacity ("Opacity", Range(0,1)) = 1
        _LifeSeconds ("Particle lifetime (s)", Float) = .34
        _HeadFrom ("Head start fraction", Float) = .40
        _HeadSeconds ("Head seconds", Float) = .10
        _ErodeFrom ("Erode from (s)", Float) = .12
        _ErodeTo ("Erode to (s)", Float) = .30
        _ErodeAlong ("Erosion follows u (tail first)", Range(0,1)) = .7
        _ErodeBias ("Erosion field bias", Range(0,.3)) = .07
        _FadeFrom ("Fade from (s)", Float) = .24
        _FadeTo ("Fade to (s)", Float) = .34
        _FlowSpeed ("Flow speed (u/s)", Float) = .9
        _CameraPush ("Push toward camera (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+40" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Combo Wave"
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
            half4 _Deep, _Mid, _Light, _Hot, _HotShade, _Outline;
            float4 _FacetTex_ST, _FacetScale, _Bands, _SliverScale;
            float _CameraPush, _EdgeRag, _EdgeTeeth, _TeethCount, _HeadHot, _HeadRound, _OutlinePx, _Aspect, _ErodeGain;
            float _Streaks, _Sheen, _SheenWidth, _SheenSeconds, _Glow, _Opacity;
            float _LifeSeconds, _HeadFrom, _HeadSeconds, _ErodeFrom, _ErodeTo, _ErodeAlong, _ErodeBias, _FadeFrom, _FadeTo, _FlowSpeed;
            CBUFFER_END
            TEXTURE2D(_FacetTex); SAMPLER(sampler_FacetTex);

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

            float Crisp(float edge, float x, float aa) { return smoothstep(edge - aa, edge + aa, x); }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float age = saturate(i.uv.z) * _LifeSeconds;
                float headT = 1 - pow(1 - saturate(age / max(.01, _HeadSeconds)), 2.2);
                float headP = lerp(_HeadFrom, 1.02, headT);
                float erodeP = 1.05 * smoothstep(_ErodeFrom, max(_ErodeFrom + .01, _ErodeTo), age);
                float fade = _Opacity * (1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), age));
                float flow = age * _FlowSpeed;

                // Грани стали: мелкие вытянуты вдоль маха и бегут за якорем, крупные — медленнее (край, распад).
                float fine = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex,
                    float2((u - flow) * _FacetScale.x, v * _FacetScale.y)).r;
                float coarse = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex,
                    float2((u - flow * .5) * _FacetScale.z + .37, v * _FacetScale.w + .21)).r;

                // Занозы стали: те же грани пака, растянутые вдоль маха ещё сильнее (штрихи и зубцы кромки).
                float sliver = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex,
                    float2((u - flow * _SliverScale.w) * _SliverScale.x + .61, v * _SliverScale.y + .13)).r;

                // Силуэт: внутренний край ровный, внешний — рваный углами граней и зубцами-занозами (рваная сталь,
                // как пенные зубцы сабли); голова тупая и скруглённая.
                float dInner = v;
                // Зубцы — пила, загнутая назад к хвосту (осколки срываются по ходу маха): глубина зубца — ячейка граней пака.
                float tu = (u - flow * .8) * _TeethCount;
                float toothCell = floor(tu);
                float toothDepth = SAMPLE_TEXTURE2D(_FacetTex, sampler_FacetTex, float2(toothCell * .0731 + .19, .43)).r;
                float tooth = (1 - frac(tu)) * (.25 + .75 * toothDepth) * smoothstep(.06, .3, u);
                float dOuter = (1 - _EdgeRag * (1 - coarse) - _EdgeTeeth * tooth) - v;
                float across = abs(v - .5) * 2;
                float dHead = (headP - u - _HeadRound * across * across) * _Aspect;
                // Поле распада: у граней льда тёмных ячеек меньше, чем у пузырьков сабли, — сдвиг вниз, чтобы дыры в
                // середине открывались так же рано, как у волны сабли.
                float field = lerp(coarse, u * .62 + coarse * .38, _ErodeAlong) - _ErodeBias;
                float dErode = (field - erodeP) * _ErodeGain;
                float d = min(min(dInner, dOuter), min(dHead, dErode));
                float px = d / max(fwidth(d), 1e-5);
                float shape = saturate(px + .5);
                float body = saturate(px - _OutlinePx + .5);

                // Полосы: глубина → кобальт → светлая сталь; границы чуть ступенятся по граням (жёстко, без волны).
                float vb = saturate(v + (fine - .5) * .06);
                float aa = max(fwidth(vb) * 1.2, .003);
                half3 col = _Deep.rgb;
                col = lerp(col, _Mid.rgb, Crisp(_Bands.x, vb, aa));
                col = lerp(col, _Light.rgb, Crisp(_Bands.y, vb, aa));
                // Плоские грани: длинная узкая ячейка чуть светлее или темнее — сталь, а не гладкая вода.
                col *= lerp(.90, 1.05, fine);

                // Металлическая «протяжка»: те же грани пака, растянутые вдоль маха ещё сильнее, — яркие длинные
                // занозы-штрихи, бегут быстрее тела (за якорем), только на теле полосы.
                float sa = max(fwidth(sliver) * .9, .002);
                float streak = Crisp(_SliverScale.z, sliver, sa) * smoothstep(.04, .25, u)
                    * Crisp(_Bands.x * .7, vb, aa) * (1 - Crisp(_Bands.z, vb, aa));
                col = lerp(col, lerp(_Light.rgb, _Hot.rgb, .6), streak * _Streaks);

                // Блик: тонкая косая полоса пробегает по стали от хвоста к голове за _SheenSeconds.
                float sheenAt = lerp(-.25, 1.25, saturate(age / max(.02, _SheenSeconds)));
                float sheenD = abs(u - sheenAt + (v - .5) * .30);
                float sw = max(fwidth(u) * 1.5, .002);
                float sheen = (1 - Crisp(_SheenWidth, sheenD, sw)) * Crisp(_Bands.y - .1, vb, aa);
                col = lerp(col, _Hot.rgb, sheen * _Sheen);

                // Горячая кромка снаружи, у головы шире (гребень за якорем); край рвётся занозами, два тона по граням.
                float crest = smoothstep(.70, 1.0, u / max(headP, .01));
                float hotFrom = _Bands.z - (coarse - .5) * _Bands.w * 2 - crest * _HeadHot - _EdgeTeeth * tooth * .8;
                float af = max(fwidth(v) * 1.2, .003);
                float hot = Crisp(hotFrom, v, af);
                half3 hotCol = lerp(_HotShade.rgb, _Hot.rgb, step(.40, fine));
                col = lerp(col, hotCol, hot);

                col = lerp(_Outline.rgb, col, body);
                float alpha = shape * lerp(_Outline.a, 1, body) * fade * i.color.a;
                half3 glow = hotCol * (hot + sheen * .5) * body * shape * fade * _Glow;
                half3 rgb = (col * alpha + glow) * i.color.rgb;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
