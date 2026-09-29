Shader "Razlom/Forest Acid Drop"
{
    Properties
    {
        _Deep ("Dense murk", Color) = (.20,.21,.03,1)
        _Bright ("Murky acid body", Color) = (.50,.52,.13,1)
        _Specular ("Wet glint", Color) = (.95,.98,.72,1)
        _Opacity ("Visible fraction", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Bright, _Specular;
                float _Opacity;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 eye = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 key = normalize(float3(-.45, .85, -.35));
                float light = saturate(dot(normal, key) * .65 + .45);
                float halfway = saturate(dot(normal, normalize(eye + key)));
                float glint = pow(halfway, 75) + pow(halfway, 18) * .13;
                float rim = pow(1 - saturate(dot(normal, eye)), 4) * .12;
                half3 color = lerp(_Deep.rgb, _Bright.rgb, light) + _Specular.rgb * (glint + rim);
                return half4(color * _Opacity, _Opacity);
            }
            ENDHLSL
        }
    }
}
