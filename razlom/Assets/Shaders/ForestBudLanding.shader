Shader "Razlom/Forest Bud Landing"
{
    Properties
    {
        _Progress("Время до падения", Range(0,1)) = 0
        _Opacity("Видимость", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Progress, _Opacity, _NeighborCount;
                float4 _NearbyDisks[8];
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 lift : TEXCOORD1; float3 positionWS : TEXCOORD2; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                float breath = .82 + .18*sin(_Progress*15.708);
                input.positionOS.y *= breath * (.65+.35*_Progress);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.lift = input.color.rg; output.uv = input.uv; return output;
            }

            float band(float r, float center, float width, float aa)
            { return 1-smoothstep(width-aa,width+aa,abs(r-center)); }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p=input.uv*2-1;
                float r=length(p), angle=atan2(p.y,p.x);
                float aa=max(fwidth(r),.003);
                float inside=1-smoothstep(.98-aa,.98+aa,r);
                // Общая граница читается и при пересечении пяти зон.
                float exposed=1;
                for (int i=0;i<(int)_NeighborCount;i++)
                {
                    float distance=length(input.positionWS.xz-_NearbyDisks[i].xy)/_NearbyDisks[i].z;
                    exposed*=smoothstep(.97,1.025,distance);
                }
                float arrival=smoothstep(0,.085,_Progress);
                float urgency=smoothstep(.7,1,_Progress);
                float pulse=.5+.5*sin(_Progress*15.708);
                float rim=band(r,.93,.035,aa)*exposed;
                float ink=band(r,.934,.047,aa)*exposed;
                float hotEdge=band(r,.935,.009,aa)*exposed;
                // Заполнение достигает неподвижной границы ровно к контакту.
                float front=lerp(.045,.875,saturate(_Progress))
                    + .13*sin(_Progress*3.14159)*(.5+.5*cos(angle*5));
                float charged=1-smoothstep(front-.07,front+.025,r);
                float wave=band(r,front,.019,aa)*(1-urgency*.7);
                float innerGlow=exp(-abs(r-.89)*18)*exposed;
                float3 color=lerp(float3(.5,.003,.012),float3(.95,.01,.028),charged);
                float veins=pow(saturate(.5+.5*cos(angle*5)),6)*smoothstep(.15,.4,r)*charged;
                color+=float3(.12,.003,.001)*veins;
                float alpha=(.4+.12*charged+.06*urgency)*inside;
                color+=innerGlow*float3(.22,.012,.004);
                color=lerp(color,float3(.055,.001,.007),ink);
                color=lerp(color,float3(1,.018,.025)*(1+.1*pulse),rim);
                color=lerp(color,float3(1,.34,.16),hotEdge*(.45+.4*urgency));
                alpha=max(alpha,ink*.94);
                // Небольшие лепестки связывают сигнал с бутоном.
                float petalAngle=abs(frac((angle+1.5708)/6.283185*5+.5)-.5);
                float petal=(1-smoothstep(.75,1,length(float2(petalAngle/.046,(r-.82)/.075))))*exposed;
                color=lerp(color,float3(1,.12,.055),petal*.8);
                alpha=max(alpha,petal*.84);
                color=lerp(color,float3(1,.09,.045),wave*.7);
                alpha=max(alpha,wave*.62);
                // Маленькое семя фиксирует центр попадания.
                float seed=1-smoothstep(.02,.027,abs(p.x)*.85+abs(p.y)*.6);
                color=lerp(color,float3(1,.34,.16),seed);
                alpha=max(alpha,seed*.9);
                if (input.lift.x>.5)
                {
                    float fingers=pow(saturate(.5+.5*cos(angle*5)),9);
                    color=lerp(float3(1,.015,.025),float3(1,.19,.055),input.lift.y);
                    alpha=pow(1-input.lift.y,2)*fingers*(.7+.15*urgency)*exposed;
                }
                return half4(color,saturate(alpha)*_Opacity*arrival*inside);
            }
            ENDHLSL
        }
    }
}
