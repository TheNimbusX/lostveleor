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
                Light light = GetMainLight(input.shadowCoord);
                half diffuse = saturate(dot(normal, light.direction));
                half shade = lerp(0.58h, 1.0h,
                    smoothstep(0.28h, 0.62h, diffuse * light.shadowAttenuation));
                float2 stoneCell = floor(input.positionWS.xz * 0.42);
                half stoneVariation = frac(sin(dot(stoneCell,
                    float2(12.9898, 78.233))) * 43758.5453);
                half3 stoneColor = lerp(_BaseColor.rgb, _AccentColor.rgb,
                    0.08h + stoneVariation * 0.10h);
                half3 color = lerp(stoneColor, _GridColor.rgb, grid * 0.46h);
                color = color * shade;

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
