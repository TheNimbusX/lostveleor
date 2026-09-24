// Мягкое тёмное пятно под предметом (владелец 24 сентября: «иначе вещи смотрятся поставленными на
// ковёр»). Плоский квад на земле умножает цвет под собой: в центре темнее, к краю — ноль.
// Форму задаёт масштаб квада (эллипс по габаритам предмета). Без текстуры и освещения.
Shader "Razlom/Camp Contact Shadow"
{
    Properties
    {
        _ShadowColor ("Цвет тени", Color) = (0.24,0.25,0.19,1)
        _Strength ("Сила", Range(0,1)) = 0.7
        _Softness ("Мягкость края", Range(0.05,1)) = 0.75
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-60" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "ContactShadow"
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _Strength;
                half _Softness;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float d = length(input.uv * 2.0 - 1.0);
                half a = (1.0h - smoothstep(1.0h - _Softness, 1.0h, d)) * _Strength;
                return half4(lerp(half3(1, 1, 1), _ShadowColor.rgb, a), 1);
            }
            ENDHLSL
        }
    }
}
