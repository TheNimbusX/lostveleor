Shader "Razlom/Squall Foam Ghost"
{
    // ПЕННЫЙ ДВОЙНИК НЕУЛОВИМОГО (Шквал v2, кадр elusive-return-3, выбор владельца
    // 02.10): полупрозрачный Пелаг из бирюзовой воды и белой пены на точке каста —
    // туда его вернёт последний прыжок; «сквозь него видно землю, с него капает пена».
    //
    // Рисуется на КОПИИ позы героя (PelagSquallGhostParts: BakeMesh скинов в свои
    // меши), сам герой этим шейдером никогда не рисуется: высветление героя владелец
    // отверг 24.09 («ужасно»).
    //
    // Язык — «морская пена» серии сабли и рывка: плоская бирюза, к кромке силуэта
    // светлее, по кромке — комковатая белая пена (пузырьковый шум пака CFXR «cfxr
    // sword trail noise bubbles» в мировых координатах, стекает вниз), без свечения
    // и дыма. Растворение — пеной сверху вниз по мягкому шуму («cfxr perlin mid»):
    // кромка растворения белеет пеной, а не чернеет.
    //
    // Числа вида — блоком свойств (MaterialPropertyBlock): _Opacity, _Dissolve,
    // _Clock (секунды жизни двойника, пена стекает), _Base (земля под ним), цвета
    // формы (_Deep/_Water/_Shallow — PelagSquallFormLook). Смешивание
    // премультиплированное, как у воды и пены семьи; глубина не пишется, задние
    // грани отсекаются.
    Properties
    {
        _Deep ("Deep water (low on the body)", Color) = (.03,.24,.32,1)
        _Water ("Water", Color) = (.06,.60,.68,1)
        _Shallow ("Shallow water (toward the rim)", Color) = (.42,.90,.92,1)
        _Foam ("Foam", Color) = (1.12,1.22,1.22,1)
        _FoamShade ("Foam shade", Color) = (.62,.86,.91,1)
        _FoamTex ("Foam noise (r), bubble cells", 2D) = "gray" {}
        _ErodeTex ("Soft noise (r), round blobs", 2D) = "gray" {}
        _BodyAlpha ("Body opacity", Range(0,1)) = .42
        _RimAlpha ("Rim foam opacity", Range(0,1)) = .95
        _Rim ("Rim foam: from, to (1 - |N.V|)", Vector) = (.45,.80,0,0)
        _FoamScale ("Foam cells per metre", Float) = 2.4
        _FlowSpeed ("Foam slides down, m/s", Float) = .35
        _Opacity ("Opacity (view)", Range(0,1)) = 1
        _Dissolve ("Dissolve (view)", Range(0,1)) = 0
        _Clock ("Seconds alive (view)", Float) = 0
        _Base ("Ground height (view), world y", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+30" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Squall Foam Ghost"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            // Тела (Razlom/Texture Toon, проход ForwardToon) пишут 1: двойник по телам не
            // рисуется никогда, как вода форм. Иначе в миг отрыва (и при посадке возврата
            // в двойника) копия позы ложится на самого героя бирюзой с белой кромкой —
            // то самое высветление, которое владелец отверг 24.09.
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
            half4 _Deep, _Water, _Shallow, _Foam, _FoamShade;
            float4 _FoamTex_ST, _ErodeTex_ST, _Rim;
            float _BodyAlpha, _RimAlpha, _FoamScale, _FlowSpeed, _Opacity, _Dissolve, _Clock, _Base;
            CBUFFER_END
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_ErodeTex); SAMPLER(sampler_ErodeTex);

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float rim = 1 - saturate(abs(dot(n, v)));
                float h = i.positionWS.y - _Base;
                // Шум по миру: «вдоль» — диагональ по земле, «вверх» — высота; пена стекает вниз.
                float2 p = float2(i.positionWS.x * .7 + i.positionWS.z * .7, h + _Clock * _FlowSpeed);
                float bubbles = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, p * _FoamScale).r;
                float soft = SAMPLE_TEXTURE2D(_ErodeTex, sampler_ErodeTex,
                    float2(i.positionWS.x - i.positionWS.z, h * .8 + _Clock * .2) * 1.3).r;

                // Вода: к кромке светлее, у ног глубже.
                half3 col = lerp(_Water.rgb, _Shallow.rgb, saturate(rim * 1.4));
                col = lerp(_Deep.rgb, col, saturate(.55 + h * .6));
                // Пена по кромке силуэта, комьями (пузырьковый шум двигает границу).
                float foam = smoothstep(_Rim.x, _Rim.y, rim + (bubbles - .3) * .35);
                half3 foamCol = lerp(_FoamShade.rgb, _Foam.rgb, smoothstep(.15, .45, bubbles));
                col = lerp(col, foamCol, foam);

                // Растворение сверху вниз по мягкому шуму; кромка растворения — пена.
                float erode = soft * .7 + (1 - saturate(h / 1.9)) * .3;
                float edge = erode + .1 - _Dissolve * 1.2;
                float keep = saturate(edge / .08);
                float froth = (1 - saturate(abs(edge - .04) / .06)) * step(.001, _Dissolve);
                col = lerp(col, foamCol, froth);

                float alpha = lerp(_BodyAlpha, _RimAlpha, max(foam, froth)) * keep * _Opacity;
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
