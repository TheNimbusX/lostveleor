Shader "Razlom/Pelag Glow"
{
    // СВЕТЯЩИЙСЯ ПРОХОД: ЦВЕТ ДОБАВЛЯЕТСЯ К ФОНУ, А НЕ ЗАМЕЩАЕТ ЕГО.
    //
    // «Razlom/Pelag Flipbook» рисует обычным альфа-смешиванием: он кладёт
    // краску поверх кадра и ярче фона стать не может — сколько ни поднимай
    // Emission, цвет упирается в белый и остаётся плоским пятном. Именно
    // поэтому увеличение размеров давало большое бежевое облако вместо
    // вспышки.
    //
    // Здесь Blend SrcAlpha One: значение прибавляется к тому, что уже
    // нарисовано. Так получаются пересвеченное ядро и свечение по краям —
    // то, чем читаются росчерки и удары.
    //
    // Пыль и дым сюда переводить НЕЛЬЗЯ: они непрозрачные по своей природе,
    // и аддитив превратит их в светящийся туман.
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)
        _Emission ("Emission", Range(0,8)) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent+30"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "PelagGlow"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Emission;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 color = sampled * _BaseColor * input.color;
                color.rgb *= _Emission;
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
