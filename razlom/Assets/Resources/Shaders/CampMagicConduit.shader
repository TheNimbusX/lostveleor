Shader "Game/Camp Magic Conduit"
{
    Properties {_FlowSpeed("Скорость потока",Float)=.7}
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+2" "RenderType"="Transparent"}
        Pass
        {
            Blend One One ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _CampMagicClock;
            CBUFFER_START(UnityPerMaterial)
            float _FlowSpeed;
            CBUFFER_END
            struct A{float4 pos:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            V Vert(A a){V o;o.pos=TransformObjectToHClip(a.pos.xyz);o.uv=a.uv;o.color=a.color;return o;}
            half4 Frag(V i):SV_Target
            {
                float cross=abs(i.uv.y*2-1);
                float edge=exp(-cross*cross*8)*smoothstep(1,.65,cross);
                float core=exp(-cross*cross*100);
                float p=frac(i.uv.x*1.8-_CampMagicClock*_FlowSpeed*.23);
                float surge=exp(-pow((p-.5)*8,2));
                float breath=.75+.13*sin(_CampMagicClock*1.4+i.uv.x*8);
                float ends=smoothstep(0,.025,i.uv.x)*smoothstep(1,.96,i.uv.x);
                half3 c=i.color.rgb*edge*(.35*breath+surge*.7)+lerp(i.color.rgb,half3(2,2.1,1.8),.62)*core*(.22+surge*.8);
                return half4(c*ends,1);
            }
            ENDHLSL
        }
    }
}
