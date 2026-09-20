Shader "Razlom/Forest Fruit Impact"
{
    Properties { _Age("Возраст", Float)=0 _Seed("Рисунок", Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Age, _Seed;
            CBUFFER_END
            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(A a) { V v; v.p=TransformObjectToHClip(a.p.xyz); v.uv=a.uv; return v; }
            half4 frag(V v):SV_Target
            {
                float2 p=v.uv*2-1;
                float r=length(p), a=atan2(p.y,p.x)+_Seed;
                float aa=max(fwidth(r),.003);
                float growth=1-pow(1-saturate(_Age/.16),3);
                float edge=(.27+.065*sin(a*5)+.035*sin(a*9+.5)) * lerp(.24,1,growth);
                float splash=1-smoothstep(edge-aa,edge+aa,r);
                float rim=smoothstep(edge-.025,edge-.010,r)*splash;
                [unroll] for(int i=0;i<7;i++)
                {
                    float angle=i*2.39996+_Seed;
                    float distance=(.39+.10*sin(i*4.7+_Seed))*growth;
                    float2 center=float2(cos(angle),sin(angle))*distance;
                    float radius=(.025+.03*(.5+.5*sin(i*7.1)))*growth;
                    splash=max(splash,1-smoothstep(radius-aa,radius+aa,length(p-center)));
                }
                float fade=1-smoothstep(.09,.38,_Age);
                float flash=(1-smoothstep(.025,.11,_Age))*(1-smoothstep(.1,.34,r));
                float waveRadius=lerp(.1,.79,saturate(_Age/.23));
                float wave=(1-smoothstep(.012,.032,abs(r-waveRadius)))*(1-smoothstep(.04,.23,_Age));
                float3 color=lerp(float3(.53,.065,.006),float3(1,.30,.018),saturate(1-r*2));
                color=lerp(color,float3(.50,.11,.008),rim*.75);
                color=lerp(color,float3(1.6,1.18,.53),flash);
                color=lerp(color,float3(1.2,.66,.15),wave);
                float alpha=max(splash*.8*fade,max(flash,wave*.85));
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
