Shader "Razlom/Sabre Foam Blob"
{
    // ПЕНА И КАПЛИ СЕРИИ САБЛИ («морская пена», вариант Б 01.10).
    //
    // Частица-билборд: мягкая маска пака (канал r — клякса «cfxr blood splash
    // dissolve», капля «cfxr water drop blur anim») режется порогом. Внутри —
    // цвет частицы с тенью у края, по краю — тонкий тёмный обвод, как у
    // волны. Порог растёт с возрастом частицы (поток AgePercent в
    // TEXCOORD0.z): клякса не тает прозрачностью, а втягивает лучи и
    // распадается — рисованная пена, а не дымка.
    Properties
    {
        _MainTex ("Shape (r)", 2D) = "white" {}
        _Outline ("Outline", Color) = (.02,.08,.11,.92)
        _Shade ("Shade near the edge", Color) = (.55,.84,.90,1)
        _CutFrom ("Cut at birth", Range(0,1)) = .32
        _CutTo ("Cut at death", Range(0,1)) = .92
        _CutPower ("Cut curve", Float) = 1.5
        _OutlineWidth ("Outline width (mask units)", Range(0,.5)) = .07
        _ShadeWidth ("Shade width (mask units)", Range(0,.6)) = .16
        // Сдвиг к камере вдоль луча взгляда, м: экранное место то же, а тела,
        // сквозь которые проходит удар, не прячут его целиком.
        _CameraPush ("Push toward camera (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+42" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Sabre Foam Blob"
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
            half4 _Outline, _Shade;
            float4 _MainTex_ST;
            float _CameraPush, _CutFrom, _CutTo, _CutPower, _OutlineWidth, _ShadeWidth;
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
                float mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).r;
                float age = saturate(i.uv.z);
                float cut = lerp(_CutFrom, _CutTo, pow(age, _CutPower));
                float d = mask - cut;
                float aa = max(fwidth(mask) * .9, 1e-4);
                float shape = smoothstep(-aa, aa, d);
                float body = smoothstep(_OutlineWidth - aa, _OutlineWidth + aa, d);
                float lit = smoothstep(_ShadeWidth - aa, _ShadeWidth + aa, d);
                half3 col = lerp(i.color.rgb * _Shade.rgb, i.color.rgb, lit);
                col = lerp(_Outline.rgb, col, body);
                float alpha = shape * lerp(_Outline.a, 1, body) * i.color.a;
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
