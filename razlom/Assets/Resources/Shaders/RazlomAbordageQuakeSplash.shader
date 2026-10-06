Shader "Razlom/Abordage Quake Splash"
{
    // ОБВАЛ, круг 4 (03.10, ревью по кадру D-quake). Круг 3 рисовал Обвал общим шейдером воды форм
    // (кольцо Пенных волн): в игре — ровное «солнце» с чёрным обводом и кремовой пилой по краю,
    // сиреневая полоса (полупрозрачный кобальт поверх охры) между героем и синим, а распад белил
    // весь трёхметровый диск в кремовую звезду. Здесь — свой шейдер всплеска (тот, общий, не трогаем):
    //  • ядро — НЕПРОЗРАЧНАЯ мокрая земля (бурая, как в D): лепестки и лучи земли от героя наружу
    //    между лопастями воды (граница из меша — TEXCOORD0.w);
    //  • вода — крупные круглые лопасти кобальта разной длины (силуэт из меша), плоские светлые
    //    пятна и струи лучами от центра (полярный шум TEXCOORD1.xy), без обвода;
    //  • белая пена — на концах лопастей (COLOR.b — вес кончика) и пятнами во внешней половине,
    //    край — круглые комья (изотропный шум по месту), не шипы;
    //  • распад — НЕ пеной: вода и земля уходят прозрачностью и дырами от центра наружу
    //    (_Hole), пена остаётся тонкой рваной каймой и рвётся на круглые капли (_RimBreak).
    // Тела пишут трафарет 1 — по ним всплеск не рисуется; низкие препятствия — как у воды форм
    // (_Over, COLOR.r). Смешивание премультиплированное.
    // КРУГ 5 (03.10, проверка круга 4 в игре):
    //  • распад ~3 кадра давал сиреневые и рыжие точки по кромке дыр — мягкая кромка (полупрозрачный кобальт
    //    и земля поверх охры). Кромка дыр и страховка — жёсткий срез (Aa, 1 px), полупрозрачной воды нет;
    //  • лучи земли читались длинными гладкими «досками» со светлыми полосами вдоль — цвет земли брался из
    //    полярного шума (вытянут вдоль луча). Теперь мокрая грязь: короткие круглые пятна трёх тонов по месту
    //    (_Mud.xy) и мелкие белые капли (_Mud.zw); полярного шума в цвете земли нет. Форма лопастей не тронута.
    //   TEXCOORD0: x — доля края лопасти ρ = r / край, y — край лопасти здесь, м, z — возраст
    //              распада, с, w — граница земли в долях края;
    //   TEXCOORD1: xy — полярный шум (лучи), z — сдвиг шума, w — радиус, м;
    //   COLOR: r — поверх низких препятствий, b — вес пены кончика, a — непрозрачность.
    Properties
    {
        _Deep ("Deep water", Color) = (.04,.10,.38,1)
        _Water ("Water", Color) = (.18,.36,.89,1)
        _Shallow ("Shallow water", Color) = (.58,.74,1,1)
        _Foam ("Foam", Color) = (1.10,1.12,1.16,1)
        _FoamShade ("Foam shade", Color) = (.70,.80,.98,1)
        _EarthDark ("Wet earth, dark", Color) = (.27,.17,.11,1)
        _Earth ("Wet earth", Color) = (.41,.27,.18,1)
        _EarthLight ("Wet earth, light patches", Color) = (.55,.38,.25,1)
        _FoamTex ("Foam noise (r), bubble cells", 2D) = "gray" {}
        _ErodeTex ("Soft noise (r), round blobs", 2D) = "gray" {}
        _Patch ("Light patches: blob scale /m, threshold, strength, ray mix", Vector) = (.9,.56,.75,.75)
        _Crest ("Tip foam: depth m, bulge m, clump scale /m, flecks", Vector) = (.44,.14,2,.9)
        _EarthEdge ("Earth: rag m, wet rim m, -, -", Vector) = (.10,.06,0,0)
        _Mud ("Wet mud: patch scale /m, fine scale /m, droplet scale /m, droplet threshold", Vector) = (.8,1.76,1,.62)
        _MudTones ("Wet mud: dark below, light above, -, -", Vector) = (.42,.64,0,0)
        _Hole ("Decay holes: start s, sweep s, hard cut (share), blob scale /m", Vector) = (.15,.28,.11,1.3)
        _HoleLead ("Hole front starts behind the centre by (share)", Float) = .5
        _RimBreak ("Rim: thins from s, drops from s, drops gone s, drop scale /m", Vector) = (.12,.27,.42,3.4)
        _FadeFrom ("Safety fade from (age, s)", Float) = .43
        _FadeTo ("Safety fade to (age, s)", Float) = .48
        _Over ("Over low obstacles: max rise m, soft m, -, tolerance m", Vector) = (1,.25,0,.3)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+36" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Abordage Quake Splash"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
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
                float2 local : TEXCOORD3;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _Deep, _Water, _Shallow, _Foam, _FoamShade, _EarthDark, _Earth, _EarthLight;
            float4 _FoamTex_ST, _ErodeTex_ST, _Patch, _Crest, _EarthEdge, _Mud, _MudTones, _Hole, _RimBreak, _Over;
            float _FadeFrom, _FadeTo, _HoleLead;
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
                // Корень — центр всплеска без поворота и масштаба: xz вершины — место от центра, м.
                o.local = v.positionOS.xz;
                o.color = v.color;
                return o;
            }

            float Bubbles(float2 p) { return SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, p).r; }
            float Soft(float2 p) { return SAMPLE_TEXTURE2D(_ErodeTex, sampler_ErodeTex, p).r; }
            float Aa(float d) { return saturate(d / max(fwidth(d), 1e-5) + .5); }
            float Smooth01(float x) { x = saturate(x); return x * x * (3 - 2 * x); }

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
                float rho = i.uv0.x, edge = max(i.uv0.y, .05), age = max(0, i.uv0.z), rhoE = i.uv0.w;
                float2 n = i.uv1.xy;
                float seed = i.uv1.z;
                float2 w = i.local + float2(seed * 1.7, seed * .9);
                float tipW = i.color.b;

                // ---- шумы: изотропные по месту (комья, пятна, дыры), полярные — лучи от центра.
                float clump = saturate(Bubbles(w * _Crest.z + float2(.11, .37)) * 2);
                float coarse = Bubbles(w * .45 + float2(.63, .19));
                float blob = Soft(w * _Patch.x + float2(.27, .71));
                float ray = Soft(n * .55 + float2(.19 + seed * .31, .43 + seed * .17));
                float ray2 = Bubbles(n * .40 + float2(.71 + seed, .53 + seed * .4));

                // ---- распад: дыры от центра наружу (вода и земля), пена кромки — каплями.
                // Фронт стартует за центром (_HoleLead): шум и срез (_Hole.z) не едят ядро раньше срока.
                float sweep = -_HoleLead + (1.3 + _HoleLead) * Smooth01((age - _Hole.x) / max(_Hole.y, 1e-3));
                float hole = Soft(w * _Hole.w + float2(.41, .13));
                float hv = rho - sweep + (hole - .5) * .45;
                // Круг 5: кромка дыры — жёсткий срез (1 px сглаживания) там, где мягкая кромка круга 4 была
                // наполовину прозрачной: полупрозрачного кобальта и земли поверх охры (сиреневых точек) нет.
                float holeKeep = Aa(hv - _Hole.z);
                float thin = 1 - .40 * Smooth01((age - _RimBreak.x) / .15);
                float br = Smooth01((age - _RimBreak.y) / max(_RimBreak.z - _RimBreak.y, 1e-3));
                float cell = Soft(w * _RimBreak.w + float2(.31 + seed * .23, .17));
                float dropKeep = age > _RimBreak.y ? Aa(saturate((cell - .28) / .45) - br) : 1;

                // ---- силуэт лопасти: круглые комья выпирают за край (по месту), не лучи.
                float inside = (1 - rho) * edge;
                float bulge = _Crest.y * (.35 + .65 * tipW) * smoothstep(.25, .70, clump);
                float shape = Aa(inside + bulge - .035 * (1 - coarse));

                // ---- земля: граница из меша (лепестки и лучи), край рваный, по ней — мокрая тёмная кромка.
                float dE = (rhoE - rho) * edge + (coarse - .5) * _EarthEdge.x + (ray - .5) * _EarthEdge.x;
                float earth = Aa(dE) * step(.001, rhoE);
                // Круг 5 (кадр D): мокрая грязь — короткие круглые пятна по месту (не полосы вдоль луча), три тона
                // с чёткой кромкой, мелкие белые капли; по краю земли — тёмная мокрая кромка, как в круге 4.
                float mud = Soft(w * _Mud.x + float2(.57, .23)) * .65 + Soft(w * _Mud.y + float2(.13 + seed * .19, .81)) * .35;
                half3 earthCol = lerp(_EarthDark.rgb, _Earth.rgb, Aa(mud - _MudTones.x));
                earthCol = lerp(earthCol, _EarthLight.rgb, Aa(mud - _MudTones.y));
                earthCol = lerp(earthCol, _EarthDark.rgb * .85, (1 - saturate(dE / max(_EarthEdge.y, .01))) * .7);
                float droplet = Aa(Bubbles(w * _Mud.z + float2(.29 + seed * .13, .61)) - _Mud.w);
                earthCol = lerp(earthCol, _Foam.rgb * .96, droplet);

                // ---- вода: от тёмного кобальта у земли к светлому у кончиков; плоские светлые пятна лучами.
                float depthT = saturate((rho - min(rhoE, .95)) / max(1 - min(rhoE, .95), .05) + (coarse - .5) * .25);
                half3 col = lerp(_Deep.rgb, _Water.rgb, smoothstep(0, .22, depthT));
                col = lerp(col, lerp(_Water.rgb, _Shallow.rgb, .35), smoothstep(.70, 1, depthT));
                float patch = smoothstep(_Patch.y, _Patch.y + .05, lerp(blob, ray, _Patch.w));
                col = lerp(col, _Shallow.rgb, patch * _Patch.z);
                col = lerp(col, lerp(_Water.rgb, _Deep.rgb, .5), smoothstep(.70, .76, ray2) * (1 - patch) * .55);

                // ---- пена: кайма на кончиках лопастей + белые пятна во внешней половине.
                float foamDepth = _Crest.x * (.25 + .75 * tipW) * (.45 + clump) * thin;
                float foam = Aa(foamDepth - inside);
                // Белые пятна пены во внешней половине (кадр D): круглые по месту, чуть вытянуты лучом.
                float fleck = smoothstep(.70, .73, lerp(Bubbles(w * 1.3 + float2(.53, .29)), ray, .25)) * smoothstep(.40, .75, rho) * _Crest.w;
                fleck *= holeKeep;
                half3 foamCol = lerp(_FoamShade.rgb, _Foam.rgb, smoothstep(.12, .45, clump));
                float foamAll = max(foam, fleck);
                col = lerp(col, foamCol, foamAll);

                // Кайма пены дыр не боится — она рвётся на капли; вода и пятна — дырами.
                float alpha = shape * lerp(holeKeep, dropKeep, foam);
                col = lerp(col, earthCol, earth);
                alpha = lerp(alpha, holeKeep, earth);

                float rise = SceneRise(i.positionCS, i.positionWS.y);
                float cap = lerp(_Over.w, _Over.x, saturate(i.color.r));
                float covered = 1 - smoothstep(cap, cap + max(_Over.y, .01), rise);
                // Страховка (круг 5) — не прозрачностью, а дырами того же шума: к _FadeTo не остаётся ничего.
                float fadeT = Smooth01((age - _FadeFrom) / max(_FadeTo - _FadeFrom, .01));
                float fade = Aa(hole + .02 - 1.05 * fadeT);
                alpha *= covered * fade * i.color.a;
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
