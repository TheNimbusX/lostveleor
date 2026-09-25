Shader "Razlom/Whirlwind Sweep"
{
    // СЕРП ВИХРЯ: ПЛОСКИЕ ЖЁСТКИЕ ТОНА ПЛЮС ЛИНИИ СКОРОСТИ И РВАНЫЙ ХВОСТ.
    //
    // Меш несёт u вдоль маха (0 — хвост, 1 — голова за клинком) и v поперёк
    // (0 — внутренний край, 1 — внешний). Поперёк лежат зоны с резкими
    // границами: тёмный обвод, кромка, середина, ядро, снова кромка и обвод.
    // Внутри кромки и середины бегут тонкие светлые линии вдоль маха — это
    // они делают форму движением, а не заливкой. Хвост стирается порогом по
    // вытянутому вдоль u шуму, поэтому рвётся на штрихи, а не на пятна.
    //
    // _Flash — кадр-вспышка: весь меш заливается ядром без обвода и без
    // эрозии. Материалы щепок и звёзд удара держат _Flash = 1 постоянно и
    // получают плоский цвет ядра.
    //
    // Смешивание премультиплированное: обвод должен быть тёмным поверх
    // светлого луга, а свечение ядра (_Glow) добавляется с нулевой альфой.
    Properties
    {
        _Core ("Core", Color) = (1.3,1.25,1.2,1)
        _Mid ("Mid", Color) = (1.3,.45,.10,1)
        _Edge ("Edge", Color) = (1.35,.12,.025,1)
        _Rim ("Rim", Color) = (.25,.08,.06,1)
        _Bands ("Bands (rim, edge, mid, core end)", Vector) = (.12,.30,.50,.84)
        _RimOuter ("Outer rim from v", Range(.5,1)) = .90
        _Head ("Revealed head fraction", Range(0,1)) = 1
        _Erode ("Erosion", Range(0,1.2)) = 0
        _ErodeAlong ("Erosion follows u", Range(0,1)) = .7
        _NoiseScale ("Noise scale (u, v)", Vector) = (16,2.5,0,0)
        _Periodic ("Periodic along u", Float) = 0
        _Streaks ("Speed lines", Range(0,1)) = .55
        _Glow ("Core glow", Range(0,2)) = .45
        _Flash ("Flash", Range(0,1)) = 0
        _Opacity ("Opacity", Range(0,1)) = 1
        _Seed ("Seed", Float) = 0
        // Маска силуэта (канал r) с жёстким порогом: форма кисти пака на
        // наших полосах. 0 — маска не используется (кольцо, щепки).
        _Mask ("Mask (r)", 2D) = "white" {}
        _MaskCut ("Mask cut", Range(0,1)) = 0
        // Профиль ширины вдоль u: серп сужается к концам (хвост u=0, острие u=1),
        // полосы пересчитываются внутри видимой ширины. 0 — ширина постоянная.
        _Taper ("Taper", Range(0,1)) = 0
        _TaperPower ("Taper power", Range(.2,3)) = .7
        _TaperSkew ("Taper skew (peak toward head)", Range(.5,2.5)) = 1.3
        // Движение внутри формы: сдвиг шума и линий скорости вдоль u (растёт
        // со временем из раскадровки) и дрожание границ полос шумом.
        _Flow ("Flow along u", Float) = 0
        _BandWobble ("Band wobble", Range(0,.3)) = 0
        // Раскадровка по времени внутри шейдера (Рассекающий): слой — частица
        // с мешем, её возраст приходит вершинным потоком AgePercent в
        // TEXCOORD0.z; при _Timed > 0 вспышка, эрозия, прозрачность, поток и
        // голова считаются от возраста (доля × _LifeSeconds), а не из
        // _Flash/_Erode/_Opacity/_Flow/_Head. Ничего не пишется в кадре: слои
        // Рассекающего, чьи свойства или трансформы менялись каждый кадр,
        // capture-плеер не рисовал вовсе (LOG 24–25.09), а частицы рисовал.
        _Timed ("Timed animation (age from particle)", Float) = 0
        _LifeSeconds ("Particle lifetime (s)", Float) = .42
        _FlashEnd ("Flash seconds", Float) = .045
        _ErodeFrom ("Erode from (s)", Float) = .10
        _ErodeTo ("Erode to (s)", Float) = .36
        _FadeFrom ("Fade from (s)", Float) = .30
        _FadeTo ("Fade to (s)", Float) = .42
        _FlowSpeed ("Flow speed (u/s)", Float) = 1.6
        _HeadFrom ("Head start fraction", Float) = 1
        _HeadSeconds ("Head seconds", Float) = .06
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+40" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Whirlwind Sweep"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // uv.z — возраст частицы 0..1 (поток AgePercent); у обычных мешей 0.
            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Core, _Mid, _Edge, _Rim;
            float4 _Bands, _NoiseScale;
            float _RimOuter, _Head, _Erode, _ErodeAlong, _Periodic, _Streaks, _Glow, _Flash, _Opacity, _Seed, _MaskCut;
            float _Taper, _TaperPower, _TaperSkew, _Flow, _BandWobble;
            float _Timed, _LifeSeconds, _FlashEnd, _ErodeFrom, _ErodeTo, _FadeFrom, _FadeTo, _FlowSpeed, _HeadFrom, _HeadSeconds;
            CBUFFER_END
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv.xyz;
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float flashP = _Flash, erodeP = _Erode, opacityP = _Opacity, flowP = _Flow, headP = _Head;
                if (_Timed > .5)
                {
                    float age = saturate(i.uv.z) * _LifeSeconds;
                    flashP = 1 - smoothstep(_FlashEnd, _FlashEnd + .02, age);
                    erodeP = 1.05 * smoothstep(_ErodeFrom, max(_ErodeFrom + .01, _ErodeTo), age);
                    opacityP = _Opacity * (1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), age));
                    flowP = age * _FlowSpeed;
                    float headT = 1 - pow(1 - saturate(age / max(.01, _HeadSeconds)), 2.2);
                    headP = lerp(_HeadFrom, 1, headT);
                }

                // Сужение: видимая ширина полосы — доля от внешнего края (v=1)
                // внутрь; вне профиля пусто, внутри v растягивается в 0..1.
                float profile = pow(max(sin(3.14159265 * pow(saturate(u), _TaperSkew)), 0), _TaperPower);
                float width = lerp(1, profile, _Taper);
                float vTaper = (v - (1 - width)) / max(width, .001);
                float taperAlive = smoothstep(-.02, .02, vTaper);
                v = saturate(vTaper);

                // Кольцо замкнуто: шум берётся по окружности, чтобы шов не читался.
                float2 noiseAt = _Periodic > .5
                    ? float2(cos(u * 6.2831853), sin(u * 6.2831853)) * _NoiseScale.x * .35 + v * _NoiseScale.y
                    : float2((u - flowP) * _NoiseScale.x, v * _NoiseScale.y);
                noiseAt += _Seed;
                float noise = .62 * ValueNoise(noiseAt) + .38 * ValueNoise(noiseAt * 2.3 + 11.3);

                // Поле эрозии: хвост (малый u) уходит первым, край рвётся штрихами.
                float field = lerp(noise, u * .62 + noise * .38, _ErodeAlong);
                float aa = max(fwidth(field) * 1.2, .004);
                float alive = smoothstep(erodeP - aa, erodeP + aa, field);

                float au = max(fwidth(u) * 1.2, .003);
                float head = 1 - smoothstep(headP - au, headP + au, u);

                // Зоны поперёк, границы жёсткие; лёгкое дрожание шумом, чтобы полосы не были линейкой.
                v = saturate(v + (noise - .5) * _BandWobble);
                float av = max(fwidth(v) * 1.2, .003);
                float inEdge = smoothstep(_Bands.x - av, _Bands.x + av, v);
                float inMid = smoothstep(_Bands.y - av, _Bands.y + av, v);
                float inCore = smoothstep(_Bands.z - av, _Bands.z + av, v);
                float pastCore = smoothstep(_Bands.w - av, _Bands.w + av, v);
                float pastRim = smoothstep(_RimOuter - av, _RimOuter + av, v);
                half3 col = _Rim.rgb;
                col = lerp(col, _Edge.rgb, inEdge);
                col = lerp(col, _Mid.rgb, inMid);
                col = lerp(col, _Core.rgb, inCore);
                col = lerp(col, _Edge.rgb, pastCore);
                col = lerp(col, _Rim.rgb, pastRim);

                // Линии скорости: тонкие светлые штрихи вдоль маха в кромке и
                // середине, слегка сбитые шумом, гуще к голове.
                float lineWave = sin((v * 5.5 + noise * .9) * 6.2831853 + (u - flowP * 2.0) * 3.0);
                float lines = smoothstep(.62, .80, lineWave) * smoothstep(.15, .7, u);
                float lineZone = inEdge * (1 - inCore);
                col = lerp(col, _Core.rgb, lines * lineZone * _Streaks);

                // Свечение ядра поверх: уходит за порог блума только красным каналом.
                float coreZone = inCore * (1 - pastCore);
                half3 glow = _Core.rgb * coreZone * _Glow;

                // Силуэт по маске: сужение концов серпа — от кисти пака, порог жёсткий.
                float maskValue = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, i.uv.xy).r;
                float am = max(fwidth(maskValue) * 1.2, .01);
                float masked = (_MaskCut > 0 ? smoothstep(_MaskCut - am, _MaskCut + am, maskValue) : 1) * taperAlive;
                float shape = alive * head * masked;
                col = lerp(col, _Core.rgb, flashP);
                float alpha = lerp(shape, masked, flashP) * opacityP * i.color.a;
                half3 rgb = (col * alpha + glow * shape * opacityP * (1 - flashP)) * i.color.rgb;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
