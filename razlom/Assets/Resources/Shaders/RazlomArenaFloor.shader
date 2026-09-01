Shader "Razlom/Arena Floor"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.10,0.11,0.16,1)
        _AccentColor ("Accent Color", Color) = (0.10,0.55,0.62,1)
        _GridColor ("Grid Color", Color) = (0.035,0.045,0.075,1)
        _GridScale ("Grid Scale", Float) = 0.25
        _GridWidth ("Grid Width", Range(0.01,0.25)) = 0.035
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ArenaForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _AccentColor;
                half4 _GridColor;
                float _GridScale;
                float _GridWidth;
            CBUFFER_END

            float4 _RazlomHeroLightPosition;
            half4 _RazlomHeroLightColor;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.shadowCoord = GetShadowCoord(position);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 coord = input.positionWS.xz * _GridScale;
                float2 edge = abs(frac(coord) - 0.5);
                float2 aa = max(fwidth(coord), 0.001);
                float grid = 1.0 - smoothstep(_GridWidth, _GridWidth + aa.x + aa.y,
                                               0.5 - max(edge.x, edge.y));

                half3 normal = normalize(input.normalWS);

                // ТЕНЬ СЧИТАЕТСЯ ВО ФРАГМЕНТЕ, А НЕ В ВЕРШИНЕ.
                //
                // Здесь стояло GetMainLight(input.shadowCoord) с координатой,
                // посчитанной в вершинном шейдере. Для персонажа это работает:
                // у него плотный меш, и между вершинами интерполировать нечего.
                //
                // Плита пола — это Cube из ВОСЬМИ вершин, растянутый на всю
                // комнату. Координата тени интерполировалась между четырьмя
                // углами через двадцать метров и не попадала ни во что; тень
                // на полу просто не появлялась. В лагере тени были, потому что
                // там геометрия авторская и на обычном URP/Lit, — оттого и
                // выглядело как «в сцене есть, в игре нет».
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light light = GetMainLight(shadowCoord);
                half diffuse = saturate(dot(normal, light.direction));
                half lit = smoothstep(0.28h, 0.62h, diffuse * light.shadowAttenuation);
                half shade = lerp(0.62h, 1.0h, lit);

                // ПОЛ ЖИВЁТ ПО ТОМУ ЖЕ ПРАВИЛУ, ЧТО И ПЕРСОНАЖ.
                //
                // Иначе персонаж стоит в холодной тени на тёплом полу и
                // выглядит вырезанным из другой картинки. Тень пола уходит в
                // синеву, освещённая часть — в тёплое; разница по тону, а не
                // по яркости, поэтому пол не проваливается в грязь.
                // Сдвиг ЕДВА заметный. Первая версия (0.66, 0.76, 1.02) вместе
                // с холодным амбиентом и Split Toning красила пол в бирюзу.
                half3 shadeTint = lerp(half3(0.92h, 0.95h, 1.04h),
                                       half3(1.03h, 1.00h, 0.96h), lit);

                // ПОЛ ОБЯЗАН НЕСТИ ДЕТАЛЬ, ИНАЧЕ ОН СЪЕДАЕТ КАДР.
                //
                // Разбор записи игры 1 сентября: пол занимает четыре пятых
                // экрана и не содержит ничего — ровная заливка с редкой
                // сеткой. На таком фоне теряется всё: и персонаж, и якорь, и
                // эффекты, которые я до этого полировал. Плотность фона —
                // не украшение, а условие, при котором видно передний план.
                //
                // Три слоя, каждый на своей частоте. Ни один не является
                // текстурой: рисовать пол художником пока некому, а
                // процедурная деталь стоит нескольких строк.

                // 1. Крупная плита: свой тон у каждой. Разброс поднят с
                // прежних 8–18% до заметного — иначе вариации попросту нет.
                float2 stoneCell = floor(input.positionWS.xz * 0.42);
                half stoneVariation = frac(sin(dot(stoneCell,
                    float2(12.9898, 78.233))) * 43758.5453);
                half3 stoneColor = lerp(_BaseColor.rgb, _AccentColor.rgb,
                    0.05h + stoneVariation * 0.26h);

                // 2. Мелкая плитка вчетверо чаще крупной: она и даёт масштаб.
                // Без неё игрок не понимает, насколько велика арена.
                float2 subCoord = input.positionWS.xz * (_GridScale * 4.0);
                float2 subEdge = abs(frac(subCoord) - 0.5);
                float2 subAA = max(fwidth(subCoord), 0.001);
                float subGrid = 1.0 - smoothstep(_GridWidth * 0.6,
                    _GridWidth * 0.6 + subAA.x + subAA.y,
                    0.5 - max(subEdge.x, subEdge.y));

                float2 subCell = floor(subCoord);
                half subVariation = frac(sin(dot(subCell,
                    float2(39.3468, 11.1357))) * 24634.6345);
                stoneColor *= lerp(0.93h, 1.07h, subVariation);

                // 3. Зерно на частоте пикселя: снимает пластиковую гладкость
                // ровной заливки. Слабое намеренно — это шум камня, а не грязь.
                half grain = frac(sin(dot(floor(input.positionWS.xz * 26.0),
                    float2(63.7264, 21.4432))) * 17324.1234);
                stoneColor *= lerp(0.965h, 1.035h, grain);

                half3 color = lerp(stoneColor, _GridColor.rgb, grid * 0.46h);
                color = lerp(color, _GridColor.rgb, subGrid * 0.17h);
                color = color * shade * shadeTint;

                // Runtime combat pool: a restrained warm pool at rest and a
                // brief HDR lift on confirmed contacts. It is explicit here
                // because this stylised shader intentionally omits URP's full
                // additional-light loop.
                float heroDistance = distance(input.positionWS,
                    _RazlomHeroLightPosition.xyz);
                half heroAttenuation = saturate(1.0h -
                    heroDistance / max(_RazlomHeroLightPosition.w, 0.001));
                heroAttenuation *= heroAttenuation;
                color += _RazlomHeroLightColor.rgb * heroAttenuation *
                    lerp(0.28h, 0.38h, grid);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
