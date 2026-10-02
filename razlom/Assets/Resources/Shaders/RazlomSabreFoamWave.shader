Shader "Razlom/Sabre Foam Wave"
{
    // ВОЛНА СЕРИИ САБЛИ — «морская пена» (вариант Б, целевой кадр 01.10,
    // ART/characters/pelag/basic-attack-2026-10-01/1-combo-target-frames.jpg).
    //
    // Меш — полумесяц пака CFXR «sword_trail 180 thick», пересчитанный под
    // сектор удара (PelagSabreComboVfxSetup): u вдоль маха (0 — хвост, где
    // клинок начал, 1 — голова за клинком), v поперёк (0 — внутренний край к
    // герою, 1 — внешний). Поперёк: тёмная глубина у внутреннего края,
    // бирюза, светлая вода с бегущими штрихами, белая пена по внешнему краю.
    // Граница пены и внешний край рвутся пузырьковым шумом пака
    // («cfxr sword trail noise bubbles»), вокруг всей формы — тонкий тёмный
    // обвод постоянной ширины в пикселях.
    //
    // Раскадровка — по возрасту частицы (вершинный поток AgePercent в
    // TEXCOORD0.z), как у серпа Рассекающего: голова выбегает за клинком,
    // хвост рассыпается пеной по тому же шуму, форма гаснет. В кадре ничего
    // не пишется.
    //
    // Смешивание премультиплированное: обвод тёмный поверх светлой земли,
    // лёгкое свечение пены добавляется с нулевой альфой.
    Properties
    {
        _Deep ("Deep water (inner edge)", Color) = (.03,.24,.32,1)
        _Water ("Water", Color) = (.06,.60,.68,1)
        _Shallow ("Shallow water", Color) = (.42,.90,.92,1)
        _Foam ("Foam", Color) = (1.12,1.22,1.22,1)
        _FoamShade ("Foam shade (bubble walls)", Color) = (.62,.86,.91,1)
        _Outline ("Outline", Color) = (.02,.08,.11,.92)
        _FoamTex ("Foam noise (r)", 2D) = "gray" {}
        _FoamScale ("Noise scale (fine u, fine v, coarse u, coarse v)", Vector) = (5,1.4,2,.7)
        _Bands ("Bands (deep end, water end, foam from, foam rag)", Vector) = (.16,.42,.66,.30)
        _EdgeRag ("Outer edge rag", Range(0,.5)) = .16
        _HeadFoam ("Extra foam at the head", Range(0,.5)) = .18
        _OutlinePx ("Outline width, px", Range(0,6)) = 2.2
        _Aspect ("Length / width", Float) = 6
        _ErodeGain ("Erosion edge in width units", Float) = 1.6
        _Streaks ("Speed streaks", Range(0,1)) = .6
        _Glow ("Foam glow", Range(0,1)) = .25
        _Opacity ("Opacity", Range(0,1)) = 1
        _LifeSeconds ("Particle lifetime (s)", Float) = .32
        _HeadFrom ("Head start fraction", Float) = .15
        _HeadSeconds ("Head seconds", Float) = .06
        _ErodeFrom ("Erode from (s)", Float) = .09
        _ErodeTo ("Erode to (s)", Float) = .28
        _ErodeAlong ("Erosion follows u (tail first)", Range(0,1)) = .7
        _FadeFrom ("Fade from (s)", Float) = .22
        _FadeTo ("Fade to (s)", Float) = .32
        _FlowSpeed ("Flow speed (u/s)", Float) = .9
        // Сдвиг к камере вдоль луча взгляда, м: экранное место то же, а тела,
        // сквозь которые проходит удар, не прячут его целиком.
        _CameraPush ("Push toward camera (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+40" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Sabre Foam Wave"
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
            half4 _Deep, _Water, _Shallow, _Foam, _FoamShade, _Outline;
            float4 _FoamTex_ST, _FoamScale, _Bands;
            float _CameraPush, _EdgeRag, _HeadFoam, _OutlinePx, _Aspect, _ErodeGain, _Streaks, _Glow, _Opacity;
            float _LifeSeconds, _HeadFrom, _HeadSeconds, _ErodeFrom, _ErodeTo, _ErodeAlong, _FadeFrom, _FadeTo, _FlowSpeed;
            CBUFFER_END
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);

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

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float age = saturate(i.uv.z) * _LifeSeconds;
                float headT = 1 - pow(1 - saturate(age / max(.01, _HeadSeconds)), 2.2);
                float headP = lerp(_HeadFrom, 1.02, headT);
                float erodeP = 1.05 * smoothstep(_ErodeFrom, max(_ErodeFrom + .01, _ErodeTo), age);
                float fade = _Opacity * (1 - smoothstep(_FadeFrom, max(_FadeFrom + .01, _FadeTo), age));
                float flow = age * _FlowSpeed;

                // Пузырьки пака: мелкий слой бежит вдоль маха, крупный — медленнее.
                float fine = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex,
                    float2((u - flow) * _FoamScale.x, v * _FoamScale.y)).r;
                float coarse = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex,
                    float2((u - flow * .5) * _FoamScale.z + .37, v * _FoamScale.w + .21)).r;

                // Силуэт: положительное расстояние внутри, в долях ширины полосы.
                // Внутренний край ровный, внешний — пенный, голова срезана по
                // раскрытию, хвост и середина рассыпаются по крупному шуму.
                float dInner = v;
                float dOuter = (1 - _EdgeRag * (1 - coarse)) - v;
                float dHead = (headP - u) * _Aspect;
                float field = lerp(coarse, u * .62 + coarse * .38, _ErodeAlong);
                float dErode = (field - erodeP) * _ErodeGain;
                float d = min(min(dInner, dOuter), min(dHead, dErode));
                // Обвод постоянной ширины в пикселях: расстояние делится на его производную.
                float px = d / max(fwidth(d), 1e-5);
                float shape = saturate(px + .5);
                float body = saturate(px - _OutlinePx + .5);

                // Полосы воды, границы дрожат пузырьками.
                float vb = saturate(v + (fine - .5) * .10);
                float aa = max(fwidth(vb) * 1.2, .003);
                half3 col = _Deep.rgb;
                col = lerp(col, _Water.rgb, smoothstep(_Bands.x - aa, _Bands.x + aa, vb));
                col = lerp(col, _Shallow.rgb, smoothstep(_Bands.y - aa, _Bands.y + aa, vb));

                // Штрихи скорости вдоль маха: тонкие светлые нити в воде, к голове гуще.
                float lineWave = sin((v * 6.0 + fine * .7) * 6.2831853 + (u - flow * 2.0) * 2.0);
                float lines = smoothstep(.72, .90, lineWave) * smoothstep(.05, .40, u)
                    * smoothstep(_Bands.x, _Bands.x + .05, vb);

                // Пена: внешняя часть, у головы её больше (гребень волны).
                float crest = smoothstep(.70, 1.0, u / max(headP, .01));
                float foamFrom = _Bands.z - (fine - .5) * _Bands.w * 2 - crest * _HeadFoam;
                float af = max(fwidth(v) * 1.2, .003);
                float foam = smoothstep(foamFrom - af, foamFrom + af, v);
                col = lerp(col, lerp(_Shallow.rgb, _Foam.rgb, .55), lines * _Streaks * (1 - foam));
                half3 foamCol = lerp(_FoamShade.rgb, _Foam.rgb, smoothstep(.28, .52, fine));
                col = lerp(col, foamCol, foam);

                col = lerp(_Outline.rgb, col, body);
                float alpha = shape * lerp(_Outline.a, 1, body) * fade * i.color.a;
                half3 glow = foamCol * foam * body * shape * fade * _Glow;
                half3 rgb = (col * alpha + glow) * i.color.rgb;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
