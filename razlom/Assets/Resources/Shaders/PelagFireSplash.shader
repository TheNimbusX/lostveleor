Shader "Razlom/Pelag Fire Splash"
{
    Properties { _Age("Age",Float)=0 _Mode("Pool",Float)=0 _Seed("Seed",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float _Age,_Mode,_Seed;
            CBUFFER_END
            V vert(A a) { V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv*2-1;return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 b=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(hash(b),hash(b+float2(1,0)),f.x),lerp(hash(b+float2(0,1)),hash(b+1),f.x),f.y);
            }
            half4 frag(V i):SV_Target
            {
                float2 q=i.uv;float angle=atan2(q.y,q.x);
                float r=length(q);
                if(_Mode>.5)
                {
                    float mottling=noise(q*5.3+_Seed), grain=noise(q*19+_Seed);
                    float edge=1-smoothstep(.77+mottling*.15,.91+mottling*.09,r);
                    float fissure=1-smoothstep(.026,.058,abs(grain-.50));
                    float heat=smoothstep(.38,.76,noise(q*7.7+float2(_Age*.09,_Seed)));
                    float ember=fissure*heat*(.72+.28*sin(_Age*4+q.x*9+q.y*11));
                    half3 col=lerp(half3(.055,.029,.018),half3(.16,.065,.021),mottling);
                    col+=half3(1.0,.18,.008)*ember*.72;
                    return half4(col,edge*(.60+.22*mottling));
                }
                float t=saturate(_Age/.32);
                float wave=sin(angle*9+_Seed)*.035+sin(angle*17)*.02;
                float rim=(1-smoothstep(.77+wave,.9+wave,r))*smoothstep(.52+wave,.72+wave,r);
                float broken=smoothstep(-.5,.3,sin(angle*6+_Seed)+sin(angle*11)*.5);
                float core=(1-smoothstep(.1,.65,r))*(1-smoothstep(0,.35,t));
                half3 col=lerp(half3(.62,.09,.015),half3(1.6,.64,.09),1-t);
                return half4(col,(rim*broken*.55+core*.4)*(1-t));
            }
            ENDHLSL
        }
    }
}
