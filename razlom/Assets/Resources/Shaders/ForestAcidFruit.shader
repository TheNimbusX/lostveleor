Shader "Razlom/Forest Acid Fruit"
{
    Properties
    {
        _BaseMap ("Existing fruit color", 2D) = "white" {}
        _BaseColor ("Rotten tint", Color) = (.72,.78,.42,1)
        _CutCenter ("Mesh bounds center", Vector) = (0,0,0,0)
        _CutScale ("Mesh bounds size", Float) = 1
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
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor, _CutCenter;
                float _CutScale;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 local : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.local = (input.positionOS.xyz - _CutCenter.xyz) / max(.001, _CutScale);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.local;
                float ragged = .025 * sin(p.y * 61 + p.z * 37) + .015 * sin(p.y * 103 - p.z * 59);
                float removed = step(.025 + ragged, p.x) * step(-.14, p.y) * step(-.23, p.z);
                clip(.5 - removed);
                half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                float light = saturate(dot(normalize(input.normalWS), normalize(float3(-.4,.8,-.3))) * .55 + .65);
                color *= lerp(half3(.50,.37,.18), half3(1.25,1.16,.83), light);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
