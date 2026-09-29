Shader "Razlom/Ground Telegraph Sector"
{
    // Сектор, круг и кольцо общих меток (GroundTelegraphView): тем же шейдером
    // рисуются кольцо воя и круг когтей Вендиго, корни Корнехвата, всплеск
    // Шипомёта. Кромки считаются в метрах от настоящей фигуры, заливка идёт от
    // внутреннего края к внешнему. Вид — общий стиль «пунктир и шевроны»
    // (GroundTelegraphStyle.hlsl): у круга засечки, у кольца и сектора шевроны
    // наружу.
    Properties
    {
        _Progress("Заполнение",Range(0,1))=0
        _Opacity("Видимость",Range(0,1))=1
        _Radius("Внешний радиус, м",Float)=2.4
        _InnerRadius("Внутренний радиус, м",Float)=0
        _Span("Раствор, рад (2π — без боковых кромок)",Float)=2.0943951
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
            float _Progress,_Opacity,_Radius,_InnerRadius,_Span,_Flash;
            CBUFFER_END
            V Vert(A a){V o;o.world=TransformObjectToWorld(a.vertex.xyz);o.position=TransformWorldToHClip(o.world);o.uv=a.uv;return o;}
            half4 Frag(V i):SV_Target
            {
                // uv.x — доля раствора, uv.y — радиус в долях внешнего. Боковые
                // кромки только у сектора: у круга и кольца шва на u=0/1 нет.
                return GroundTelegraphArc(i.uv,i.world.xz,_Radius,_InnerRadius,_Span,_Progress,_Opacity,1,_Flash);
            }
            ENDHLSL
        }
    }
}
