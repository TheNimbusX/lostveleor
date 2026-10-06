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
    // Ревью 02.10, вечер («буря — непонятно… дольше и более явно», волны стали 3 и 2,5 с): поле
    // плотнее (подкраска гуще к удару), фронт заливки — широкий горячий гребень, в каждом укрытии
    // золотая дуга-отсчёт по часовой с севера (замкнулась — удар), последнюю секунду поле и
    // золото бьются пульсом всё чаще (_Pulse — ThicketStormDangerRules.PulseOf, от тика Sim).
    //
    // Ревью 02.10, вечер: «круг укрытия у босса розовый, а остальные золотые». Розовым его заливала аура
    // канала у ног босса (частицы под полем, кольцо r 2,6 м через круг 0). Теперь аура — здесь же, под
    // полем (_Aura: кольцо, полоса, пульсы от тела), а внутри любого укрытия гаснет до лёгкой розовой
    // дымки (SDAuraInShelter): кромка и дуга-отсчёт у всех кругов — одно золото.
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
        _Floor("Пол поляны: центр x, z, полуоси, м",Vector)=(0,0,10.49,7.87)
        _Pulse("Пульс перед ударом: волна 1, волна 2",Vector)=(0,0,0,0)
        _Aura("Аура у ног босса: радиус, м, видимость, секунды канала, пульс",Vector)=(2.645,0,0,0)
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
            float4 _WaveA,_WaveB,_Safe0,_Safe1,_Safe2,_Safe3,_Safe4,_Safe5,_Source,_Floor,_Pulse,_Aura;
            CBUFFER_END

            static const float SDStep=2.6;       // шаг шевронов в ряду, м
            static const float SDRow=2.25;       // шаг рядов (соты: 2,6·√3/2)
            static const float SDClear=.8;       // от кромки укрытия до середины шеврона, м
            static const float SDFloorField=.72; // шеврон — только до этого поля пола (|x|⁴+|z|⁴ в полуосях): не у самой кромки
            static const float SDRimWidth=.09;   // золотая кромка укрытия изнутри, м
            static const float SDRimGlow=.2;     // тёплый свет от неё внутрь, м
            static const float SDWash=.12;       // ровная подкраска опасности под заливкой метки (×0,75…1,25 к удару)
            static const float SDCrest=.9;       // гребень фронта заливки: хвост за фронтом, м
            static const float SDDialGap=.16;    // дуга-отсчёт: от золотой кромки внутрь, м
            static const float SDDialWidth=.07;  // толщина дуги-отсчёта, м
            static const float SDAuraLine=.07;   // кольцо ауры у ног босса: полутолщина линии, м
            static const float SDAuraBand=.4;    // мягкая полоса за кольцом (на 1,08 радиуса), м
            static const float SDAuraPulse=.9;   // пульс бежит от тела (0,3 радиуса) к кольцу за столько секунд, их три
            static const float SDAuraInShelter=.18; // внутри укрытия аура — только лёгкая розовая дымка

            V Vert(A a){V o;o.world=TransformObjectToWorld(a.vertex.xyz);o.position=TransformWorldToHClip(o.world);o.uv=a.uv;return o;}

            // Ближнее укрытие: d — метры от его кромки (+ снаружи, в опасности; − внутри).
            void SDNearest(float2 p,float4 c,inout float d,inout float4 shelter)
            {
                float e=c.w>.5?length(p-c.xy)-c.z:1e4;
                bool closer=e<d;
                d=closer?e:d;
                shelter=closer?c:shelter;
            }

            // Слой одной волны, цвет с уже умноженной альфой. wave: заливка, видимость, вспышка, золото;
            // pulse — пульс последней секунды перед ударом (0…1).
            float4 SDLayer(float2 p,float px,float4 c0,float4 c1,float4 c2,float4 wave,float pulse)
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
                // Под меткой — ровная подкраска всей опасности: пол целиком красноват с первого тика,
                // пока заливка ещё у босса (у малых меток её роль играет кромка рядом); к удару и на
                // пульсе — гуще.
                float pxD=max(length(float2(ddx(d),ddy(d))),1e-4);
                float outside=saturate(d/pxD+.5);
                float shown=saturate(wave.y);
                float prog=saturate(wave.x);
                float wash=SDWash*(.75+.5*prog)*(1+.8*pulse)*outside*shown*(1-a);
                pm+=float3(.40,.010,.002)*wash;
                a+=wash;

                // Фронт заливки — широкий горячий гребень за ним (общая метка рисует только тонкую черту):
                // в долгой волне видно, как опасность идёт к краю поляны. Во вспышке удара — нет.
                float front=(fill-prog)*fillLength;
                float gate=saturate(prog*60)*(1-smoothstep(.97,1,prog))*(1-step(1e-3,wave.z));
                float crest=exp(-max(-front,0)/SDCrest)*saturate(-front/px+.5)*gate;
                GTOver(pm,a,float3(1.15,.08,.016),crest*.2*outside*shown);
                // Пульс последней секунды: вся опасность бьётся красным, всё чаще к удару.
                GTOver(pm,a,float3(1.0,.05,.012),pulse*.26*outside*shown);

                // Укрытие — чистая земля без заливки; по самой кромке изнутри золото и тёплый свет
                // от него внутрь (не «жёлтая тарелка»: середина круга не светится); на пульсе ярче.
                float din=-d;
                float hole=saturate(din/pxD+.5)*step(.5,shelter.w);
                float rw=max(SDRimWidth,2.5*pxD);
                float core=hole*saturate((rw-din)/pxD+.5);
                float glow=hole*exp(-max(din-rw,0)/SDRimGlow);
                GTOver(pm,a,float3(.95,.62,.18),glow*(.26+.22*pulse)*rim);
                GTOver(pm,a,float3(2.4,1.55,.5)*(1+.4*wave.z+.5*pulse),core*.95*rim);

                // Дуга-отсчёт внутри золотой кромки: по часовой с севера, замыкается в тик удара; ещё не
                // пройденная часть — тусклая дорожка, по ней видно, сколько осталось.
                float u=frac(.25-atan2(rel.y,rel.x)/6.2831853);
                float dialIn=rw+SDDialGap, dialW=max(SDDialWidth,2*pxD);
                float dial=hole*saturate((din-dialIn)/pxD+.5)*saturate((dialIn+dialW-din)/pxD+.5);
                float done=saturate((prog-u)*circ/px+.5)*saturate(prog*60);
                GTOver(pm,a,float3(.55,.36,.10),dial*.22*rim*(1-done));
                GTOver(pm,a,float3(2.2,1.45,.45)*(1+.4*pulse),dial*.9*rim*done);
                return float4(pm,a);
            }

            // Аура канала у ног босса (центр — _Source, в бурю он стоит), цвет с умноженной альфой: розовое
            // кольцо, полоса за ним и три пульса от тела наружу, ярче на пульсе перед ударом волны. Внутри
            // любого укрытия — лёгкая дымка: кромку укрытия рисует золото поля.
            float4 SDAura(float2 p,float px)
            {
                float shown=saturate(_Aura.y);
                float R=max(_Aura.x,.1);
                float r=length(p-_Source.xy);
                float w=max(SDAuraLine,1.5*px);
                float ring=saturate(1-abs(r-R)/w)*.95;
                float k=(r-R*1.08)/SDAuraBand;
                float band=exp(-k*k)*.3;
                float pulses=0;
                [unroll] for(int n=0;n<3;n++)
                {
                    float ph=frac(_Aura.z/SDAuraPulse+n/3.0);
                    float out_=1-(1-ph)*(1-ph);
                    float pr=R*(.3+.7*out_);
                    pulses+=saturate(1-abs(r-pr)/(1.4*w))*(1-ph)*(1-ph)*.55;
                }
                pulses*=.6+.4*saturate(_Aura.w);
                float d=1e4;
                float4 shelter=float4(0,0,1,0);
                SDNearest(p,_Safe0,d,shelter);SDNearest(p,_Safe1,d,shelter);SDNearest(p,_Safe2,d,shelter);
                SDNearest(p,_Safe3,d,shelter);SDNearest(p,_Safe4,d,shelter);SDNearest(p,_Safe5,d,shelter);
                float hole=saturate(-d/px+.5);
                float a=saturate(ring+band+pulses)*shown*lerp(1,SDAuraInShelter,hole);
                return float4(float3(1.6,1.12,1.28)*a,a);
            }

            half4 Frag(V i):SV_Target
            {
                float2 p=i.world.xz;
                float px=GTPixel(p);
                // Условие — из материала, одно на весь кадр: невидимую волну не считаем (обе разом —
                // только несколько тиков на стыке волн).
                float4 first=0, second=0;
                [branch] if(_WaveA.y+_WaveA.w>0) first=SDLayer(p,px,_Safe0,_Safe1,_Safe2,_WaveA,saturate(_Pulse.x));
                [branch] if(_WaveB.y+_WaveB.w>0) second=SDLayer(p,px,_Safe3,_Safe4,_Safe5,_WaveB,saturate(_Pulse.y));
                // Новые укрытия чисты сразу: вспышка ударившей первой волны в них не заходит —
                // герой видит, куда бежать дальше, а не где его только что задело.
                float dB=1e4;
                float4 shelterB=float4(0,0,1,0);
                SDNearest(p,_Safe3,dB,shelterB);SDNearest(p,_Safe4,dB,shelterB);SDNearest(p,_Safe5,dB,shelterB);
                first*=1-saturate(-dB/px+.5)*step(.5,shelterB.w)*saturate(_WaveB.w);
                // Волна 2 поверх волны 1: её круги встают в тик удара первой, вспышка первой гаснет под ней.
                float3 pm=second.rgb+first.rgb*(1-second.a);
                float a=second.a+first.a*(1-second.a);
                // Аура канала — под обеими волнами.
                [branch] if(_Aura.y>0)
                {
                    float4 aura=SDAura(p,px);
                    pm+=aura.rgb*(1-a);
                    a+=aura.a*(1-a);
                }
                return half4(pm/max(a,1e-4),a*saturate(i.uv.x));
            }
            ENDHLSL
        }
    }
}
