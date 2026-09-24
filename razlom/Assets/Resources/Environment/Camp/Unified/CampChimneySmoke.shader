Shader "Razlom/Camp Chimney Smoke"
{
    Properties { _BaseMap("Smoke density",2D)="white"{} _Density("Opacity multiplier",Range(0,4))=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            half _Density;
            struct Attributes { float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR; };
            Varyings Vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;o.color=v.color;return o;}
            half4 Frag(Varyings i):SV_Target { half density=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).r;return half4(i.color.rgb,saturate(density*i.color.a*_Density)); }
            ENDHLSL
        }
    }
}
