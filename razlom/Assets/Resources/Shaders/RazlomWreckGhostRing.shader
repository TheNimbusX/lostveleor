Shader "Razlom/Wreck Ghost Ring"
{
    // КОЛЬЦО УДАРА Призрачного якоря v2 (07.10, вместо «шестерёнки»; Editor/PelagWreckComboVfxSetup.Ghost). Частица-меш
    // (диск в XZ, u — угол без повторов, v — r / 1.45 радиуса Sim; возраст — поток AgePercent в TEXCOORD0.z), маска
    // WreckGhost_RingShock.png (artifacts/wreck/v4/forms-vfx/ghost/tools/gen_ring.py):
    //  • r — форма: тонкая линия кольца и рваные шипы разной длины и наклона; порог растёт с возрастом — линия худеет и
    //    рвётся в тонких местах, шипы втягиваются с кончиков. Цвет по глубине от края: фиолетовый #8E7BFF → бледный
    //    #D9CCFF → белое HDR ядро (лёгкий bloom). Чернил нет;
    //  • g — ореол: две ступени светлого фиолетового светом (сложение), гаснет ступенями (порог), не дымкой;
    //  • b — свет земли внутри круга (пол арены не берёт точечные огни URP): ступени светлого фиолетового — глазурь
    //    (земля холодеет и светлеет, как под фиолетовым светом), вспыхивает сразу и сжимается порогом к кольцу и центру.
    Properties
    {
        _MainTex ("Ring mask (r shape, g glow, b ground light)", 2D) = "black" {}
        _White ("Hot core (HDR)", Color) = (1.65,1.55,2.1,1)
        _Pale ("Pale violet (#D9CCFF)", Color) = (.69,.60,1,1)
        _Violet ("Violet edge (#8E7BFF)", Color) = (.27,.20,1,1)
        _GlowColor ("Glow (additive)", Color) = (.30,.24,.85,1)
        _WashColor ("Ground light (glaze colour)", Color) = (.30,.25,.95,1)
        _WashGlaze ("Ground light: glaze opacity at full step", Range(0,1)) = .55
        _CutFrom ("Cut at birth", Range(0,1)) = .03
        _CutTo ("Cut at death", Range(0,1)) = .95
        _CutPower ("Cut curve", Float) = 1.6
        _PaleW ("Pale from (depth past the cut)", Range(0,1)) = .10
        _CoreW ("White from (depth past the cut)", Range(0,1)) = .32
        _GlowSteps ("Glow (inner thr, outer thr, inner gain, outer gain)", Vector) = (.55,.22,.75,.38)
        _GlowLife ("Glow shrinks (from age, to age)", Vector) = (.15,.80,0,0)
        _WashLevels ("Ground light steps", Float) = 4
        _WashLife ("Ground light (rise age, shrink from, shrink to)", Vector) = (.04,.20,.95,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+30" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Ghost Ring"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
            half4 _White, _Pale, _Violet, _GlowColor, _WashColor;
            float4 _MainTex_ST, _GlowSteps, _GlowLife, _WashLife;
            float _CutFrom, _CutTo, _CutPower, _PaleW, _CoreW, _WashLevels, _WashGlaze;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv.xyz;
                o.color = v.color;
                return o;
            }

            float Crisp(float edge, float x, float aa) { return smoothstep(edge - aa, edge + aa, x); }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).rgb;
                float age = saturate(i.uv.z);

                // Форма: порог по возрасту, цвет по глубине от края среза.
                float cut = lerp(_CutFrom, _CutTo, pow(age, _CutPower));
                float d = m.r - cut;
                float aa = max(fwidth(m.r) * .9, 1e-4);
                float shape = Crisp(0, d, aa);
                half3 col = lerp(_Violet.rgb, _Pale.rgb, Crisp(_PaleW, d, aa));
                col = lerp(col, _White.rgb, Crisp(_CoreW, d, aa));
                col *= i.color.rgb;

                // Ореол: две ступени, порог растёт — ступени сжимаются к линии.
                float gs = saturate((age - _GlowLife.x) / max(1e-3, _GlowLife.y - _GlowLife.x));
                float ag = max(fwidth(m.g) * .9, 1e-4);
                float inner = Crisp(lerp(_GlowSteps.x, 1.02, gs), m.g, ag);
                float outer = Crisp(lerp(_GlowSteps.y, 1.02, gs), m.g, ag);
                half3 add = _GlowColor.rgb * (inner * _GlowSteps.z + outer * _GlowSteps.w);

                // Свет земли: ступени, вспыхивает за _WashLife.x, потом порог съедает тусклое к кольцу и центру.
                float rise = saturate(age / max(1e-3, _WashLife.x));
                float ws = saturate((age - _WashLife.y) / max(1e-3, _WashLife.z - _WashLife.y));
                float aw = max(fwidth(m.b) * .9, 1e-4);
                float level = floor(m.b * _WashLevels) / _WashLevels;
                float wash = level * Crisp(ws * .98 + .02, m.b, aw) * rise;
                float glaze = wash * _WashGlaze;

                // Над формой — форма; вне её — глазурь света земли (премультипл.) и ореол светом.
                float a = i.color.a;
                half3 outside = (_WashColor.rgb * glaze + add) * (1 - shape);
                return half4((col * shape + outside) * a, (shape + glaze * (1 - shape)) * a);
            }
            ENDHLSL
        }
    }
}
