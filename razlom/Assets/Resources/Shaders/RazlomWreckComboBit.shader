Shader "Razlom/Wreck Combo Bit"
{
    // ИСКРЫ, ВСПЫШКА И СКОЛЫ МАХОВ КРУШЕНИЯ (06.10 вечер, Editor/PelagWreckComboVfxSetup).
    //
    // Копия приёмов «Razlom/Sabre Foam Blob» (сам шейдер сабли не трогается), два режима:
    //  • _ShapeAlpha 0 — мягкая маска пака в канале r режется порогом, растущим с возрастом частицы (поток
    //    AgePercent в TEXCOORD0.z): по краю тёмный обвод, у края тень, внутри цвет частицы, в сердцевине —
    //    горячее ядро (_Core). Вспышка «cfxr spikes impact dissolve» втягивает лучи, искра «cfxr stretch spike
    //    fade» укорачивается — рисованно, а не дымкой;
    //  • _ShapeAlpha 1 — сколы и комья листа «cfxr debris unlit 3x3»: силуэт из альфы, три ступени канала r —
    //    внешнее кольцо обводом, среднее — скос (светлее цвета частицы), середина — грань цвета частицы.
    Properties
    {
        _MainTex ("Shape (r, or alpha)", 2D) = "white" {}
        _ShapeAlpha ("Shape from alpha (debris sheet)", Float) = 0
        _MaskGain ("Mask gain (r)", Float) = 1
        _Outline ("Ink outline", Color) = (.02,.04,.12,.94)
        _Shade ("Shade near the edge (x colour)", Color) = (.45,.60,.90,1)
        _Core ("Hot core", Color) = (1.15,1.22,1.32,1)
        _CutFrom ("Cut at birth", Range(0,1)) = .20
        _CutTo ("Cut at death", Range(0,1)) = .90
        _CutPower ("Cut curve", Float) = 1.4
        _OutlineWidth ("Outline width (mask units)", Range(0,.5)) = .06
        _ShadeWidth ("Shade width (mask units)", Range(0,.6)) = .14
        _CoreWidth ("Core from (mask units, >1 = none)", Range(0,2)) = .30
        _BevelMul ("Debris bevel: colour gain", Float) = 1.8
        _BevelAdd ("Debris bevel: added light", Color) = (.10,.14,.22,1)
        _CameraPush ("Push toward camera (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+42" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Wreck Combo Bit"
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
            half4 _Outline, _Shade, _Core, _BevelAdd;
            float4 _MainTex_ST;
            float _ShapeAlpha, _MaskGain, _CameraPush, _CutFrom, _CutTo, _CutPower, _OutlineWidth, _ShadeWidth, _CoreWidth, _BevelMul;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

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
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy);
                if (_ShapeAlpha > .5)
                {
                    // Скол / ком: силуэт из альфы, ступени r — обвод, скос, грань.
                    float aa = max(fwidth(tex.a) * .9, 1e-4);
                    float shape = smoothstep(.5 - aa, .5 + aa, tex.a);
                    float ra = max(fwidth(tex.r) * .9, 1e-3);
                    // Лист пака в sRGB: ступени 128 / 202 / 255 читаются как .22 / .59 / 1.
                    float bevel = smoothstep(.40 - ra, .40 + ra, tex.r);
                    float face = smoothstep(.80 - ra, .80 + ra, tex.r);
                    half3 bevelCol = i.color.rgb * _BevelMul + _BevelAdd.rgb;
                    half3 col = lerp(_Outline.rgb, lerp(bevelCol, i.color.rgb, face), bevel);
                    float alpha = shape * lerp(_Outline.a, 1, bevel) * i.color.a;
                    return half4(col * alpha, alpha);
                }
                float mask = saturate(tex.r * _MaskGain);
                float age = saturate(i.uv.z);
                float cut = lerp(_CutFrom, _CutTo, pow(age, _CutPower));
                float d = mask - cut;
                float aa2 = max(fwidth(mask) * .9, 1e-4);
                float shape2 = smoothstep(-aa2, aa2, d);
                float body = smoothstep(_OutlineWidth - aa2, _OutlineWidth + aa2, d);
                float lit = smoothstep(_ShadeWidth - aa2, _ShadeWidth + aa2, d);
                float core = smoothstep(_CoreWidth - aa2, _CoreWidth + aa2, d);
                half3 col2 = lerp(i.color.rgb * _Shade.rgb, i.color.rgb, lit);
                col2 = lerp(col2, _Core.rgb, core);
                col2 = lerp(_Outline.rgb, col2, body);
                float alpha2 = shape2 * lerp(_Outline.a, 1, body) * i.color.a;
                return half4(col2 * alpha2, alpha2);
            }
            ENDHLSL
        }
    }
}
