Shader "Game/Forest Portal"
{
    Properties
    {
        _BaseColor ("Цвет глубины", Color) = (.035,.19,.16,1)
        _GlowColor ("Цвет потоков", Color) = (.17,.72,.53,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _GlowColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half fog:TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.uv=v.uv; o.fog=ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.uv*2-1;
                float r=length(p), angle=atan2(p.y,p.x);
                float flow=.5+.5*sin(angle*3+r*14-_Time.y*.9+sin(angle*5-r*9+_Time.y*.4));
                float thread=pow(flow,7)*smoothstep(.12,.7,r);
                float depth=.5+.5*sin(r*19-_Time.y*.6+sin(angle*2));
                half3 color=_BaseColor.rgb*(.55+depth*.45)+_GlowColor.rgb*(thread*.65+depth*.08);
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
