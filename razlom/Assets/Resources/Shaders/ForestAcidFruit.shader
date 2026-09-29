Shader "Razlom/Forest Acid Fruit"
{
    // Осколки гнилой кожуры в живой луже V6: настоящая сетка плода Плюй-плода,
    // от которой оставлена одна «шапка» сферы с рваным краем. Снаружи — кожура
    // (текстура плода, тон гнили), изнутри (обратная грань) — бледная мякоть,
    // у самого края кожура светлеет, как надорванная. Направление шапки,
    // центр и размер сетки — на каждый рендерер через MaterialPropertyBlock.
    Properties
    {
        _BaseMap ("Existing fruit color", 2D) = "white" {}
        _BaseColor ("Гниющая кожура", Color) = (.55,.40,.85,1)
        _Pulp ("Мякоть изнутри", Color) = (.80,.74,.38,1)
        _CutCenter ("Mesh bounds center", Vector) = (0,0,0,0)
        _CutScale ("Mesh bounds size", Float) = 1
        _ShardDir ("Шапка: направление (xyz) и косинус края (w)", Vector) = (0,1,0,.62)
        _ShardSeed ("Шапка: зерно рваного края", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor, _Pulp, _CutCenter, _ShardDir;
                float _CutScale, _ShardSeed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 local : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.local = (input.positionOS.xyz - _CutCenter.xyz) / max(.001, _CutScale);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                bool front = IS_FRONT_VFACE(face, true, false);
                float3 d = normalize(input.local + 1e-5);
                float ragged = .07 * sin(d.x * 19 + d.z * 13 + _ShardSeed * 5.1) + .045 * sin(d.y * 37 - d.x * 23 + _ShardSeed * 2.3);
                float keep = dot(d, _ShardDir.xyz) - _ShardDir.w - ragged;
                clip(keep);
                float rim = 1 - saturate(keep / .1);
                half3 skin = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float3 normal = normalize(input.normalWS) * (front ? 1 : -1);
                half shade = saturate(dot(normal, light.direction) * .5 + .6) * lerp(.6, 1, light.shadowAttenuation);
                half3 color = front ? lerp(skin, _Pulp.rgb * .9, rim * .55) : _Pulp.rgb * lerp(.62, 1, rim);
                return half4(color * shade, 1);
            }
            ENDHLSL
        }
    }
}
