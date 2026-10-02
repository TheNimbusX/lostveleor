Shader "Razlom/Thicket Storm Danger"
{
    // Буря цветения Хозяина Чащи — где урон, где укрытие (владелец 02.10, п. 12: «буря
    // цветения не особо читается где урон а где сейв зона»). Пока волна впереди, весь пол
    // поляны — одна метка удара общим языком (GroundTelegraphStyle.hlsl, «пунктир и
    // шевроны»): заливка растёт от босса к краям поляны к тику удара, у кромок укрытий —
    // светящийся пунктир по тёмной обводке, шевроны сотами остриём к ближнему укрытию и
    // загораются, когда до них дошла заливка; под всем — лёгкая ровная подкраска, чтобы
    // пол был красноват с первого тика. Круги света (до трёх на волну) вырезаны чисто:
    // внутри — земля как есть, по кромке изнутри — золото с коротким светом (середина не
    // светится — без «жёлтой тарелки»). Две волны — два слоя: в тик удара первой встают
    // круги второй, вспышка первой гаснет под ними; шевроны ударившей волны снимаются
    // в тик удара, её золото гаснет за 3 тика.
    //
    // Вид — ThicketStormDangerView: сетка на пол поляны (uv.x — доля «поле есть», край
    // пола растворяется), векторы ниже — каждый кадр из Sim. Без света; под всеми
    // прозрачными эффектами и общими метками (Transparent−15), персонажей не перекрывает.
    Properties
    {
        _WaveA("Волна 1: заливка, видимость, вспышка, золото укрытий",Vector)=(0,0,0,0)
        _WaveB("Волна 2: заливка, видимость, вспышка, золото укрытий",Vector)=(0,0,0,0)
        _Safe0("Волна 1, укрытие 1: x, z, радиус, есть",Vector)=(0,0,0,0)
        _Safe1("Волна 1, укрытие 2: x, z, радиус, есть",Vector)=(0,0,0,0)
        _Safe2("Волна 1, укрытие 3: x, z, радиус, есть",Vector)=(0,0,0,0)
        _Safe3("Волна 2, укрытие 1: x, z, радиус, есть",Vector)=(0,0,0,0)
        _Safe4("Волна 2, укрытие 2: x, z, радиус, есть",Vector)=(0,0,0,0)
        _Safe5("Волна 2, укрытие 3: x, z, радиус, есть",Vector)=(0,0,0,0)
        _Source("Откуда заливка: x, z мира, длина, м",Vector)=(0,0,12,0)
        _Floor("Пол поляны: центр x, z, полуоси, м",Vector)=(0,0,10,7.5)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-15" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            float4 _WaveA,_WaveB,_Safe0,_Safe1,_Safe2,_Safe3,_Safe4,_Safe5,_Source,_Floor;
            CBUFFER_END

            static const float SDStep=2.6;       // шаг шевронов в ряду, м
            static const float SDRow=2.25;       // шаг рядов (соты: 2,6·√3/2)
            static const float SDClear=.8;       // от кромки укрытия до середины шеврона, м
            static const float SDFloorField=.72; // шеврон — только до этого поля пола (|x|⁴+|z|⁴ в полуосях): не у самой кромки
            static const float SDRimWidth=.07;   // золотая кромка укрытия изнутри, м
            static const float SDRimGlow=.14;    // короткий тёплый свет от неё внутрь, м
            static const float SDWash=.07;       // ровная подкраска опасности под заливкой метки

            V Vert(A a){V o;o.world=TransformObjectToWorld(a.vertex.xyz);o.position=TransformWorldToHClip(o.world);o.uv=a.uv;return o;}

            // Ближнее укрытие: d — метры от его кромки (+ снаружи, в опасности; − внутри).
            void SDNearest(float2 p,float4 c,inout float d,inout float4 shelter)
            {
                float e=c.w>.5?length(p-c.xy)-c.z:1e4;
                bool closer=e<d;
                d=closer?e:d;
                shelter=closer?c:shelter;
            }

            // Слой одной волны, цвет с уже умноженной альфой. wave: заливка, видимость, вспышка, золото.
            float4 SDLayer(float2 p,float px,float4 c0,float4 c1,float4 c2,float4 wave)
            {
                float d=1e4;
                float4 shelter=float4(0,0,1,0);
                SDNearest(p,c0,d,shelter);SDNearest(p,c1,d,shelter);SDNearest(p,c2,d,shelter);

                // Пунктир кромки — по окружности ближнего укрытия, штрихов целое число (шва нет).
                float2 rel=p-shelter.xy;
                float circ=6.2831853*max(shelter.z,.1);
                float2 dash=GTDash((atan2(rel.y,rel.x)/6.2831853+.5)*circ,circ,px);

                // Заливка — от босса к краю поляны: к удару фронт доходит до края.
                float fillLength=max(_Source.z,1);
                float fill=saturate(length(p-_Source.xy)/fillLength);

                // Шевроны сотами, каждый остриём к ближнему к нему укрытию: «беги туда».
                float row=floor(p.y/SDRow+.5);
                float odd=frac(row*.5)*2;
                float2 cell=float2((floor(p.x/SDStep-.5*odd+.5)+.5*odd)*SDStep,row*SDRow);
                float dc=1e4;
                float4 toward=float4(0,0,1,0);
                SDNearest(cell,c0,dc,toward);SDNearest(cell,c1,dc,toward);SDNearest(cell,c2,dc,toward);
                float2 to=toward.xy-cell;
                float2 dir=to*rsqrt(max(dot(to,to),1e-6));
                float2 off=p-cell;
                float2 q=float2(dot(off,float2(dir.y,-dir.x)),dot(off,dir));
                float reach=length(cell-_Source.xy);
                float lit=smoothstep(reach-.5*GTChevronDepth,reach+.5*GTChevronDepth,saturate(wave.x)*fillLength);
                float4 mark=GTChevronMark(GTChevron(q),px,lit);
                float2 f=(cell-_Floor.xy)/max(_Floor.zw,1e-3);
                f*=f;
                // Ударившая волна (идёт её вспышка) шевронов не держит: в тик удара они не спорят
                // с шевронами новой волны. Появляются и гаснут при снятии — вместе с золотом укрытий.
                float rim=saturate(wave.w);
                mark.xy*=step(SDClear,dc)*step(f.x*f.x+f.y*f.y,SDFloorField)*step(.5,toward.w)*rim*(1-step(1e-3,wave.z));

                half4 danger=GroundTelegraph(d,dash,fill,fillLength,mark,wave.x,wave.y,1,wave.z);
                float3 pm=danger.rgb*danger.a;
                float a=danger.a;
                // Под меткой — лёгкая ровная подкраска всей опасности: пол целиком красноват с первого
                // тика, пока заливка ещё у босса (у малых меток её роль играет кромка рядом).
                float pxD=max(length(float2(ddx(d),ddy(d))),1e-4);
                float wash=SDWash*saturate(d/pxD+.5)*saturate(wave.y)*(1-a);
                pm+=float3(.40,.010,.002)*wash;
                a+=wash;

                // Укрытие — чистая земля без заливки; по самой кромке изнутри золото и короткий свет
                // от него внутрь (не «жёлтая тарелка»: середина круга не светится).
                float din=-d;
                float hole=saturate(din/pxD+.5)*step(.5,shelter.w);
                float rw=max(SDRimWidth,2.5*pxD);
                float core=hole*saturate((rw-din)/pxD+.5);
                float glow=hole*exp(-max(din-rw,0)/SDRimGlow);
                GTOver(pm,a,float3(.95,.62,.18),glow*.26*rim);
                GTOver(pm,a,float3(2.4,1.55,.5)*(1+.4*wave.z),core*.95*rim);
                return float4(pm,a);
            }

            half4 Frag(V i):SV_Target
            {
                float2 p=i.world.xz;
                float px=GTPixel(p);
                // Условие — из материала, одно на весь кадр: невидимую волну не считаем (обе разом —
                // только несколько тиков на стыке волн).
                float4 first=0, second=0;
                [branch] if(_WaveA.y+_WaveA.w>0) first=SDLayer(p,px,_Safe0,_Safe1,_Safe2,_WaveA);
                [branch] if(_WaveB.y+_WaveB.w>0) second=SDLayer(p,px,_Safe3,_Safe4,_Safe5,_WaveB);
                // Новые укрытия чисты сразу: вспышка ударившей первой волны в них не заходит —
                // герой видит, куда бежать дальше, а не где его только что задело.
                float dB=1e4;
                float4 shelterB=float4(0,0,1,0);
                SDNearest(p,_Safe3,dB,shelterB);SDNearest(p,_Safe4,dB,shelterB);SDNearest(p,_Safe5,dB,shelterB);
                first*=1-saturate(-dB/px+.5)*step(.5,shelterB.w)*saturate(_WaveB.w);
                // Волна 2 поверх волны 1: её круги встают в тик удара первой, вспышка первой гаснет под ней.
                float3 pm=second.rgb+first.rgb*(1-second.a);
                float a=second.a+first.a*(1-second.a);
                return half4(pm/max(a,1e-4),a*saturate(i.uv.x));
            }
            ENDHLSL
        }
    }
}
