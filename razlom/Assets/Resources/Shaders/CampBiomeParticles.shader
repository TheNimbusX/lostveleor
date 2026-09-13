Shader "Game/Camp Biome Particles"
{
    Properties{_BaseMap("Рисунок снежинки",2D)="white"{} _Textured("Использовать рисунок",Float)=0}
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+4" "RenderType"="Transparent"}
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float _Textured;
            CBUFFER_END
            struct A{float4 p:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            V Vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.color=a.color;return o;}
            half4 Frag(V v):SV_Target
            {
                float2 p=v.uv*2-1;float mask=1-smoothstep(.35,.9,length(p));
                if(_Textured>.5)mask=saturate((SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,v.uv).r-.22)/.78);
                return half4(v.color.rgb,v.color.a*mask);
            }
            ENDHLSL
        }
    }
}
