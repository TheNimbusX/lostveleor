Shader "Razlom/Blade Fire"
{
    Properties
    {
        _BaseMap ("Arcadia texture", 2D) = "white" {}
        _Atlas ("4x4 animated atlas", Float) = 1
        _Heat ("Heat", Range(0,1)) = 1
        _Energy ("Emission", Float) = 2
        _Halo ("Glow", Float) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_TexelSize;
                float _Atlas, _Heat, _Energy, _Halo;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; o.color=v.color; return o;
            }
            half4 AtlasSample(float2 uv, float frame)
            {
                float2 cell=float2(fmod(frame,4),3-floor(frame/4));
                uv=clamp(uv,.003,.997);
                return SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,(uv+cell)*.25);
            }
            half4 Frag(Varyings i):SV_Target
            {
                if (_Atlas < .5)
                {
                    half alpha=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a;
                    return half4(i.color.rgb*_Energy,alpha*i.color.a*_Heat);
                }
                float time=frac(_Time.y)*16;
                float f=floor(time), next=fmod(f+1,16), blend=frac(time);
                half4 a=lerp(AtlasSample(i.uv,f),AtlasSample(i.uv,next),blend);
                if (_Atlas > 1.5)
                    return half4(a.rgb*i.color.rgb*_Energy,a.a*i.color.a*_Heat);
                // Ореол из того же авторского атласа читается даже без экранного Bloom.
                half3 halo=0;
                const float2 offsets[4]={float2(.09,0),float2(-.09,0),float2(.045,.018),float2(-.045,-.018)};
                for (int k=0;k<4;k++)
                {
                    half4 h=lerp(AtlasSample(i.uv+offsets[k],f),AtlasSample(i.uv+offsets[k],next),blend);
                    halo+=h.rgb*h.a;
                }
                half3 energy=a.rgb*a.a*_Energy+halo*(_Halo*.25);
                return half4(energy,_Heat);
            }
            ENDHLSL
        }
    }
}
