Shader "Razlom/Wendigo Warning"
{
    Properties { _Progress("Заполнение",Range(0,1))=0 _Opacity("Видимость",Range(0,1))=1 _IsSector("Сектор",Float)=1 _Impact("Попадание",Float)=0 }
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
            struct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
            struct V {float4 position:SV_POSITION;float2 uv:TEXCOORD0;};
            CBUFFER_START(UnityPerMaterial)
            float _Progress,_Opacity,_IsSector,_Impact;
            CBUFFER_END
            V Vert(A a){V o;o.position=TransformObjectToHClip(a.vertex.xyz);o.uv=a.uv;return o;}
            half4 Frag(V i):SV_Target
            {
                float u=i.uv.x,r=i.uv.y;
                float rim=smoothstep(.955,.977,r)*(1-smoothstep(.985,1,r));
                float side=(1-smoothstep(.008,.02,min(u,1-u)))*_IsSector*smoothstep(.08,.2,r);
                float fill=(1-smoothstep(_Progress-.025,_Progress+.025,r))*.25;
                float moving=(1-smoothstep(.01,.025,abs(r-_Progress)))*.5;
                float pulse=.88+.12*sin(_Progress*19);
                float notch=pow(max(0,cos(u*6.283185*8)),24)*smoothstep(.82,.93,r)*(1-smoothstep(.94,.96,r))*(1-_IsSector);
                float a=saturate((rim*.96+side*.8+fill+moving*.28+notch*.8)*pulse)*_Opacity;
                float3 red=lerp(float3(.58,.035,.035),float3(1,.36,.13),saturate(rim+side+notch));
                red=lerp(red,float3(1,.78,.47),saturate(_Impact*12)*(1-_Impact));
                return half4(red,a);
            }
            ENDHLSL
        }
    }
}
