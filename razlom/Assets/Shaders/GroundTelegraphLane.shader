Shader "Razlom/Ground Telegraph Lane"
{
    Properties
    {
        _Progress("Заполнение к старту",Range(0,1))=0
        _Opacity("Видимость",Range(0,1))=1
        _Length("Длина, м",Float)=8
        _Width("Ширина, м",Float)=1.9
        _Consumed("Пройденная часть полосы",Range(0,1))=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+12" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off Offset -1,-1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GroundTelegraphStyle.hlsl"
            struct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
            struct V {float4 position:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;};
            CBUFFER_START(UnityPerMaterial)
            float _Progress,_Opacity,_Length,_Width,_Consumed;
            CBUFFER_END
            V Vert(A a){V o;o.world=TransformObjectToWorld(a.vertex.xyz);o.position=TransformWorldToHClip(o.world);o.uv=a.uv;return o;}
            half4 Frag(V i):SV_Target
            {
                float edge=min(min(i.uv.x,1-i.uv.x)*_Width,min(i.uv.y,1-i.uv.y)*_Length);
                float arrowY=lerp(.14,.87,saturate(_Progress));
                float arrow=1-smoothstep(.028,.048,abs(i.uv.y-arrowY+abs(i.uv.x-.5)*.25));
                arrow*=1-smoothstep(.19,.23,abs(i.uv.x-.5));
                float remaining=_Consumed<=0?1:smoothstep(_Consumed-.01,_Consumed+.01,i.uv.y);
                return GroundTelegraph(i.world.xz,edge,i.uv.y,_Progress,_Opacity*remaining,1,arrow);
            }
            ENDHLSL
        }
    }
}
