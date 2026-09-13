Shader "Game/Camp Rift Motes"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+1" "RenderType"="Transparent" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz); o.uv=input.uv*2-1; o.color=input.color; return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p=abs(input.uv);
                float soft=exp2(-dot(p,p)*9);
                float star=pow(saturate(1-p.x),20)*pow(saturate(1-p.y),3)
                    +pow(saturate(1-p.y),20)*pow(saturate(1-p.x),3);
                return half4(input.color.rgb*input.color.a*(soft*.5+star*.6),0);
            }
            ENDHLSL
        }
    }
}
