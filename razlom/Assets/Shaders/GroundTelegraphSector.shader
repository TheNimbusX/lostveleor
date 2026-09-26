Shader "Razlom/Ground Telegraph Sector"
{
    // Сектор, круг и кольцо общих меток (GroundTelegraphView). Ветка сектора
    // из WendigoWarning.shader, плюс внутренний радиус: тем же шейдером
    // рисуется кольцо воя Вендиго. Кромки считаются в метрах от настоящей
    // фигуры, заливка идёт от внутреннего края к внешнему.
    Properties
    {
        _Progress("Заполнение",Range(0,1))=0
        _Opacity("Видимость",Range(0,1))=1
        _Radius("Внешний радиус, м",Float)=2.4
        _InnerRadius("Внутренний радиус, м",Float)=0
        _Span("Раствор, рад (2π — без боковых кромок)",Float)=2.0943951
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
            float _Progress,_Opacity,_Radius,_InnerRadius,_Span;
            CBUFFER_END
            V Vert(A a){V o;o.world=TransformObjectToWorld(a.vertex.xyz);o.position=TransformWorldToHClip(o.world);o.uv=a.uv;return o;}
            half4 Frag(V i):SV_Target
            {
                // uv.x — доля раствора, uv.y — радиус в долях внешнего.
                float r=i.uv.y*_Radius;
                float edge=_Radius-r;
                if(_InnerRadius>0)edge=min(edge,r-_InnerRadius);
                // Боковые кромки только у сектора: у круга и кольца шва на u=0/1 нет.
                // Дальше прямого угла от кромки ближайшая точка луча — его начало.
                if(_Span<6.27)edge=min(edge,sin(min(min(i.uv.x,1-i.uv.x)*_Span,1.5707963))*r);
                float fill=saturate((r-_InnerRadius)/max(.01,_Radius-_InnerRadius));
                return GroundTelegraph(i.world.xz,edge,fill,_Progress,_Opacity,1,0);
            }
            ENDHLSL
        }
    }
}
