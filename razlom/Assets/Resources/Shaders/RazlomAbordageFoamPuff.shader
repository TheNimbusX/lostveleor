Shader "Razlom/Abordage Foam Puff"
{
    // ПЕНА ГЕЙЗЕРА СЛИТЫМИ МАССАМИ (Абордаж v2, круг 4, 03.10). Ревью в игре: пена — россыпь отдельных
    // кремовых овалов («яиц»), у каждого свой чёрный обвод и светлая кайма — на шейдере пены сабли каждая
    // частица рисует обвод и тень по своему краю. Здесь клуб пены — пара слоёв одного выброса:
    //  • _Back = 1 — силуэт: маска пака, раздутая на _Dilate, целиком цвета обвода; материал раньше в
    //    очереди — все силуэты ложатся до всех заливок, и обвод остаётся только по краю объединения;
    //  • _Back = 0 — заливка без обвода: белая пена, тень снизу (свет сверху: маска, взятая на _ShadeOffset
    //    ниже, у нижнего края клуба кончается раньше), мягкая, без линии по краю — перекрытые клубы
    //    читаются одной пухлой массой с тенями под буграми (кадр F), а не яйцами.
    // Порог маски растёт с возрастом (поток AgePercent в TEXCOORD0.z), как у пены сабли: клуб втягивается,
    // силуэт — вместе с ним. Клуб без поворота (вид выбрасывает rotation 0): «низ» маски = низ экрана.
    Properties
    {
        _MainTex ("Shape (r)", 2D) = "white" {}
        _Outline ("Outline", Color) = (.02,.08,.11,.92)
        _Shade ("Shade under the bumps", Color) = (.64,.95,.82,1)
        _CutFrom ("Cut at birth", Range(0,1)) = .24
        _CutTo ("Cut at death", Range(0,1)) = .82
        _CutPower ("Cut curve", Float) = 2.2
        _Back ("Silhouette pass (1) or fill (0)", Float) = 0
        _Dilate ("Silhouette dilation (mask units)", Float) = .09
        _ShadeOffset ("Shade sample offset down (uv)", Float) = .045
        _ShadeSoft ("Shade softness (mask units)", Float) = .30
        _ShadeStrength ("Shade strength", Range(0,1)) = .85
        _CameraPush ("Push toward camera (m)", Float) = .1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+42" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Abordage Foam Puff"
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
            float _CutFrom, _CutTo, _CutPower, _Back, _Dilate, _ShadeOffset, _ShadeSoft, _ShadeStrength, _CameraPush;
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
                float cut = lerp(_CutFrom, _CutTo, pow(saturate(i.uv.z), _CutPower)) - _Back * _Dilate;
                float d = mask - cut;
                float aa = max(fwidth(mask) * .9, 1e-4);
                float shape = smoothstep(-aa, aa, d);
                if (_Back > .5)
                {
                    float a = shape * _Outline.a * i.color.a;
                    return half4(_Outline.rgb * a, a);
                }
                // Тень снизу: ниже этой точки клуб кончается — значит, это нижний бок бугра.
                float below = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy - float2(0, _ShadeOffset)).r;
                float lit = smoothstep(cut - .05, cut + _ShadeSoft, below);
                half3 col = lerp(i.color.rgb * lerp(half3(1, 1, 1), _Shade.rgb, _ShadeStrength), i.color.rgb, lit);
                float alpha = shape * i.color.a;
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
