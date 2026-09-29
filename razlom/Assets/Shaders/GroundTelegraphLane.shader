Shader "Razlom/Ground Telegraph Lane"
{
    Properties
    {
        _Progress("Заполнение к старту",Range(0,1))=0
        _Opacity("Видимость",Range(0,1))=1
        _Length("Длина, м",Float)=8
        _Width("Ширина, м",Float)=1.9
        _Consumed("Пройденная часть полосы",Range(0,1))=0
        _Flash("Вспышка контакта",Range(0,1))=0
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
            float _Progress,_Opacity,_Length,_Width,_Consumed,_Flash;
            CBUFFER_END
            V Vert(A a){V o;o.world=TransformObjectToWorld(a.vertex.xyz);o.position=TransformWorldToHClip(o.world);o.uv=a.uv;return o;}
            half4 Frag(V i):SV_Target
            {
                // uv.x — поперёк полосы, uv.y — вдоль, от моба. Шевроны по оси
                // остриём по ходу удара загораются, когда до них дошла заливка.
                // Пройденная тараном часть (_Consumed) гаснет целиком.
                float remaining=_Consumed<=0?1:smoothstep(_Consumed-.01,_Consumed+.01,i.uv.y);
                return GroundTelegraphLane(i.uv,i.world.xz,_Length,_Width,_Progress,_Opacity*remaining,_Flash);
            }
            ENDHLSL
        }
    }
}
