Shader "Game/Camp Altar Flame"
{
    Properties
    {
        _BaseMap ("Flame Pro atlas", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o;
            }
            half4 Frame(float2 uv,float frame)
            {
                float2 cell=float2(fmod(frame,8),5-floor(frame/8));
                half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,(clamp(uv,.004,.996)+cell)/float2(8,6));
                c.a = saturate(c.a * 2.1);
                // Небольшая чаша читается с игровой камеры за счёт горячего тела пламени.
                c.rgb = lerp(c.rgb, c.rgb * half3(1.15,.86,.5),.4) * c.a * 1.5;
                return c;
            }
            half4 Sequence(float2 uv,float phase)
            {
                float t=phase*47;
                float frame=floor(t);
                return lerp(Frame(uv,frame),Frame(uv,min(frame+1,47)),frac(t));
            }
            half4 Frag(Varyings i):SV_Target
            {
                // Две фазы: вес каждой равен нулю в момент её перемотки.
                // Нулевая производная веса убирает и скачок, и резкое изменение скорости на стыке.
                float phase=frac(_Time.y*1.15/2);
                float weight=sin(phase*PI);
                weight*=weight;
                return Sequence(i.uv,phase)*weight+Sequence(i.uv,frac(phase+.5))*(1-weight);
            }
            ENDHLSL
        }
    }
}
