Shader "Game/Camp Hover Outline"
{
    // Контур наведения для мест лагеря без собственной обводки (палатка, стол, доска).
    // Один проход, три материала (CampServiceNpc.HullMaterial), порядок — очередью:
    //   маска (Transparent-6) — проекция предмета пишет бит 8 трафарета, цвет не трогает;
    //   линия (Transparent-5) — вывернутая оболочка рисует только там, где бита нет,
    //                           то есть строго СНАРУЖИ силуэта: внутренних кромок нет;
    //   сброс (Transparent-4) — тот же меш стирает бит: тун-шейдеры и VFX Пелага
    //                           сравнивают байт целиком (Ref 1 NotEqual).
    // Бит 8 — из пользовательских битов URP [0..3]; бит 0 занят тун-шейдерами.
    Properties
    {
        _Color("Цвет контура", Color) = (1,.72,.25,.45)
        _Width("Толщина в пикселях", Float) = 1.4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
        _ColorMask("ColorMask", Float) = 15
        _StencilRef("Stencil Ref", Float) = 8
        _StencilReadMask("Stencil ReadMask", Float) = 8
        _StencilWriteMask("Stencil WriteMask", Float) = 8
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil Comp", Float) = 6
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass("Stencil Pass", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "CampHoverOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull [_Cull]
            ZWrite Off
            ZTest [_ZTest]
            ColorMask [_ColorMask]
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilReadMask]
                WriteMask [_StencilWriteMask]
                Comp [_StencilComp]
                Pass [_StencilPass]
                Fail Keep
                ZFail Keep
            }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Width;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Та же экранная выдавка, что у арки (CampArchOutline): ширина в пикселях
                // не зависит от дальности. Маска и сброс идут с _Width = 0 — без сдвига,
                // поэтому их растеризация совпадает бит в бит.
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 direction = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                direction /= max(length(direction), .001);
                output.positionCS.xy += direction * (_Width * 2 / _ScreenParams.xy) * output.positionCS.w;
                return output;
            }
            half4 Frag(Varyings input):SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
