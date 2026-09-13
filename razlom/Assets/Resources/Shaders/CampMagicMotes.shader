Shader "Game/Camp Magic Motes"
{
    Properties {_Style("Форма",Float)=0}
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+3" "RenderType"="Transparent"}
        Pass
        {
            Blend One One ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Style;
            CBUFFER_END
            struct A{float4 pos:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            V Vert(A a){V o;o.pos=TransformObjectToHClip(a.pos.xyz);o.uv=a.uv;o.color=a.color;return o;}
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;float r=length(p);float density;
                if(_Style<.5) density=exp(-dot(p,p)*16)+.25*exp(-abs(p.x)*55-abs(p.y)*4)+.25*exp(-abs(p.y)*55-abs(p.x)*4);
                else if(_Style<1.5) density=exp(-dot(p,p)*4)*smoothstep(1,.65,r);
                else density=(exp(-pow((r-.62)*18,2))*.6+exp(-dot(p-float2(-.25,.25),p-float2(-.25,.25))*80))*.7;
                return half4(i.color.rgb*i.color.a*density,1);
            }
            ENDHLSL
        }
    }
}
