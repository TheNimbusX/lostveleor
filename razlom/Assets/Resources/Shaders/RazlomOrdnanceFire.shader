Shader "Razlom/Ordnance Fire"
{
    Properties
    {
        _MainTex ("Authored flame", 2D) = "white" {}
        _Energy ("Hot core", Range(1,4)) = 2.4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+30" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half _Energy;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR;};
            struct Varyings {float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR;};
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.color=v.color;return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                half4 painted=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                half alpha=painted.a*i.color.a;
                float2 cell=frac(i.uv*4);
                alpha*=smoothstep(0,.035,min(min(cell.x,1-cell.x),min(cell.y,1-cell.y)));
                half core=smoothstep(.32,.85,min(painted.r,painted.g));
                half3 color=painted.rgb*i.color.rgb*lerp(1,_Energy,core);
                return half4(color*alpha,alpha);
            }
            ENDHLSL
        }
    }
}
