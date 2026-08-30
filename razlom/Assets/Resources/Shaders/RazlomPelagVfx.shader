Shader "Razlom/Pelag VFX"
{
    Properties
    {
        _BaseColor ("Edge", Color) = (1,0.30,0.22,0.8)
        _CoreColor ("Core", Color) = (1,0.93,0.78,1)
        _Intensity ("Intensity", Range(0.5,2)) = 1
        _Softness ("Core Width", Range(0.05,0.95)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PelagVfx"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _CoreColor;
                float _Intensity;
                float _Softness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float across = abs(input.uv.y * 2.0 - 1.0);
                float core = 1.0 - smoothstep(0.0, max(0.05, _Softness), across);
                float cap = smoothstep(0.0, 0.06, input.uv.x) * smoothstep(0.0, 0.08, 1.0 - input.uv.x);
                half4 color = lerp(_BaseColor, _CoreColor, core);
                color.rgb *= _Intensity;
                color.a *= input.color.a * cap * (1.0 - across * 0.35);
                return color;
            }
            ENDHLSL
        }
    }
}
