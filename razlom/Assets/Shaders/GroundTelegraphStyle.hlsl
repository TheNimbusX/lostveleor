#ifndef RAZLOM_GROUND_TELEGRAPH_STYLE
#define RAZLOM_GROUND_TELEGRAPH_STYLE
// Общий вид меток на земле — «B — пунктир и шевроны» (выбор владельца 29.09,
// кадры 02a–02c в ART/characters/act-1-enemies/review/mobs-v2-round2-2026-09-29/
// telegraphs). Действует на все метки: полосы (таран Камнекопыта, линия
// Шипомёта), круги (корни Корнехвата, всплеск Шипомёта, прыжок и круг когтей
// Вендиго, посадка плода), кольцо воя, секторы (коготь Вендиго и общий вид).
//
// Кромка — светящийся пунктир из коротких оранжево-красных штрихов (горячая
// середина, красные края) поверх тонкой тёмной обводки по самой границе удара:
// на тёмной траве край держат штрихи, на светлой земле — обводка. Внутри —
// мягкая полупрозрачная тёплая заливка, трава видна. Время: заливка растёт от
// источника (полоса — от моба, круг — из центра, кольцо — от внутреннего края,
// сектор — от вершины), а знаки внутри загораются, когда она до них дошла, —
// до того они тусклый контур. Знаки: шевроны остриём по ходу удара (в полосе
// вдоль, в секторе и кольце воя — наружу, «беги отсюда») и засечки у кромки
// круга.
//
// Все размеры — метры мира, одни у любой фигуры: штрихи и шевроны не
// растягиваются по размеру метки. Число штрихов на ребре целое, штрих — на
// каждом конце (углы полосы и сектора — уголками, шов круга не виден).
// Анимации сверх таймера нет: ни бегущего пунктира, ни пульса; вспышка
// контакта — только по флагу вида. Один проход, без текстур.
// Прежние стили (25.09 и трещины 44111e2b) лежат в истории git.

static const float GTDashPeriod=.36;   // штрих + просвет: ~0,22 + ~0,14 м
static const float GTDashDuty=.6;
static const float GTOutline=.018;     // тёмная обводка, не тоньше двух пикселей
static const float GTDashWidth=.055;   // толщина штриха, не тоньше трёх пикселей
static const float GTChevronWidth=.6;
static const float GTChevronDrop=.36;  // насколько концы плеч отстают от острия
static const float GTChevronThick=.24; // толщина плеча по оси удара
static const float GTChevronDepth=GTChevronDrop+GTChevronThick;
static const float GTChevronStep=1.2;  // шаг шевронов вдоль полосы и в ряду
static const float GTRowStep=1.1;      // шаг рядов в секторе и кольце
static const float GTRimClear=.4;      // от кромки до шеврона
static const float GTSideClear=.3;     // от луча сектора до шеврона
static const float GTTickNear=.2, GTTickFar=.38, GTTickWidth=.045, GTTickStep=.55;

// Пиксель в метрах — для сглаживания координат без гладкой производной (угол
// у шва круга).
float GTPixel(float2 worldXZ)
{
    return max(1e-4,.5*(length(ddx(worldXZ))+length(ddy(worldXZ))));
}

// Штрихи вдоль ребра длиной len, s — метры вдоль него от начала. x — штрих,
// y — его мягкий ореол.
float2 GTDash(float s,float len,float px)
{
    float period=max(len,1e-3)/max(1,round(len/GTDashPeriod));
    float t=abs(frac(s/period+.5)-.5)*period;
    float h=.5*GTDashDuty*period;
    return float2(saturate((h-t)/px+.5),saturate((h+.03-t)/.06));
}

// Шеврон остриём по +y, центр его рамки — 0. Знаковое расстояние, м (<0 внутри).
float GTChevron(float2 q)
{
    const float k=2*GTChevronDrop/GTChevronWidth;
    const float c=rsqrt(1+k*k);
    float top=.5*GTChevronDepth-k*abs(q.x);
    return max(max((q.y-top)*c,(top-GTChevronThick-q.y)*c),abs(q.x)-.5*GTChevronWidth);
}

// Знак для сборки: x — тусклый контур, y — тело, z — насколько зажжён, w —
// близость к середине штриха (там он горячий).
float4 GTChevronMark(float sdf,float px,float lit)
{
    const float k=2*GTChevronDrop/GTChevronWidth;
    float w=max(.02,1.3*px);
    return float4(saturate(.5-(abs(sdf+.5*w)-.5*w)/px),saturate(.5-sdf/px),lit,
        saturate(-sdf*2*sqrt(1+k*k)/GTChevronThick));
}

void GTOver(inout float3 pm,inout float a,float3 c,float k)
{
    k=saturate(k);
    pm=c*k+pm*(1-k);
    a=k+a*(1-k);
}

// Сборка метки. d — метры от границы внутрь; dash — штрих и ореол ближнего
// ребра (GTDash); fill — заливка 0..1 от источника, fillLength — её длина в
// метрах; mark — знак внутри (GTChevronMark); exposed — видна ли кромка
// (соседние круги плода её прячут); flash — вспышка контакта (0..1).
half4 GroundTelegraph(float d,float2 dash,float fill,float fillLength,float4 mark,
    float progress,float opacity,float exposed,float flash)
{
    float pxD=max(length(float2(ddx(d),ddy(d))),1e-4);
    float p=saturate(progress);
    float inside=saturate(d/pxD+.5);
    float urgency=smoothstep(.8,1,p);
    float front=(fill-p)*fillLength;             // м: + ещё впереди, − уже позади
    float started=saturate(p*60);
    float filled=(1-smoothstep(-.1,.1,front))*started;
    float behind=max(-front,0);
    float nearFront=exp(-behind/.7)*filled;
    float3 pm=0;
    float a=0;
    // Заливка: слабый тон на всей фигуре, плотнее и теплее — где время прошло.
    float3 fc=lerp(float3(.30,.008,.002),lerp(float3(.40,.010,.002),float3(.80,.035,.007),nearFront*.45),filled);
    fc=lerp(fc,float3(.80,.035,.007),.6*flash);
    GTOver(pm,a,fc,(.11+filled*(.22+.07*nearFront+.06*urgency)+.25*flash)*inside);
    // Мягкий свет у кромки изнутри.
    GTOver(pm,a,float3(.95,.035,.007),exp(-max(d-GTOutline,0)/.2)*(.10+.06*filled)*exposed*inside);
    // Фронт заливки — тонкая светлая черта; у коротких фигур гаснет, там время
    // видно по знакам.
    float gate=started*(1-smoothstep(.97,1,p))*saturate((fillLength-1.2)/1.8);
    float ft=(front+.04)/.04;
    GTOver(pm,a,float3(1.6,.09,.015),(exp(-ft*ft)*.28+exp(-behind/.3)*filled*.06)*gate*inside);
    // Знак: зажжённый — горячая середина и красные края, тусклый — контур.
    float3 sc=lerp(float3(1.4,.06,.013),lerp(float3(1.35,.04,.008),float3(2.6,.5,.15),smoothstep(.1,.8,mark.w)),mark.z);
    GTOver(pm,a,sc*(1+.3*flash),(mark.x*.7*(1-mark.z)+mark.y*.96*mark.z)*inside);
    // Кромка: тёмная обводка по самой границе, за ней штрих, внутрь — его ореол.
    float ow=max(GTOutline,2*pxD), dw=max(GTDashWidth,3*pxD);
    float outline=inside*saturate((ow-d)/pxD+.5);
    float band=saturate((d-ow)/pxD+.5)*saturate((ow+dw-d)/pxD+.5);
    float t=saturate(abs(d-ow-.5*dw)/(.5*dw));
    float3 dc=lerp(float3(2.6,.75,.25),float3(1.8,.05,.010),smoothstep(.15,.8,t))*(1+.15*urgency+.3*flash);
    float halo=exp(-max(d-ow-dw,0)/.045)*saturate((d-ow-dw)/pxD+.5);
    GTOver(pm,a,float3(1.2,.04,.008),halo*dash.y*.45*exposed);
    GTOver(pm,a,float3(.010,.003,.002),outline*.94*exposed);
    GTOver(pm,a,dc,band*dash.x*.97*exposed);
    return half4(pm/max(a,1e-4),a*saturate(opacity));
}

// Полоса: uv.x — поперёк (0..1), uv.y — вдоль от источника (0..1). Шевроны по
// оси остриём по ходу удара, шаг около 1,2 м (сегменту Шипомёта 1,75 м — один).
half4 GroundTelegraphLane(float2 uv,float2 worldXZ,float laneLength,float laneWidth,
    float progress,float opacity,float flash)
{
    float px=GTPixel(worldXZ);
    float x=(uv.x-.5)*laneWidth, y=uv.y*laneLength;
    float side=.5*laneWidth-abs(x), ends=min(y,laneLength-y);
    float2 dash=side<ends?GTDash(y,laneLength,px):GTDash(x+.5*laneWidth,laneWidth,px);
    float n=max(1,floor(laneLength/GTChevronStep)), pitch=laneLength/n;
    float centre=(min(floor(y/pitch),n-1)+.5)*pitch;
    float lit=smoothstep(centre-.5*GTChevronDepth,centre+.5*GTChevronDepth,saturate(progress)*laneLength);
    float4 mark=GTChevronMark(GTChevron(float2(x,y-centre)),px,lit);
    // Узкой полосе шеврон не по размеру.
    if(laneWidth<GTChevronWidth+.3)mark.xy=0;
    return GroundTelegraph(min(side,ends),dash,uv.y,laneLength,mark,progress,opacity,1,flash);
}

// Дуга: uv.x — доля раствора (0,5 — взгляд), uv.y — радиус в долях внешнего.
// Полный раствор без внутреннего радиуса — круг, у него засечки у кромки;
// кольцо и сектор — ряды шевронов остриём от центра.
half4 GroundTelegraphArc(float2 uv,float2 worldXZ,float radius,float inner,float span,
    float progress,float opacity,float exposed,float flash)
{
    float px=GTPixel(worldXZ);
    bool full=span>6.27;
    float r=uv.y*radius, a=(uv.x-.5)*span, outer=radius-r;
    float d=outer;
    float2 dash=GTDash(uv.x*span*radius,span*radius,px);
    if(inner>0&&r-inner<d)
    {
        d=r-inner;
        dash=GTDash(uv.x*span*inner,span*inner,px);
    }
    if(!full)
    {
        // До ближнего луча; дальше прямого угла от него ближайшая точка — вершина.
        float toSide=min(min(uv.x,1-uv.x)*span,1.5707963);
        float e=sin(toSide)*r;
        if(e<d)
        {
            d=e;
            dash=GTDash(r*cos(toSide)-inner,radius-inner,px);
        }
    }
    float fillLength=max(.01,radius-inner);
    float p=saturate(progress);
    float4 mark=0;
    if(full&&inner<=0)
    {
        // Засечки — короткие радиальные штрихи у кромки, число кратно четырём.
        float n=max(8,4*round(6.2831853*(radius-.5*(GTTickNear+GTTickFar))/(4*GTTickStep)));
        float lateral=abs(frac(uv.x*n+.5)-.5)*6.2831853*r/n;
        float body=saturate((.5*GTTickWidth-lateral)/px+.5)*saturate((outer-GTTickNear)/px+.5)
            *saturate((GTTickFar-outer)/px+.5);
        mark=float4(body,body,smoothstep(radius-GTTickFar,radius-GTTickNear,p*radius),
            saturate(1-lateral/(.5*GTTickWidth)));
    }
    else
    {
        // Ряды шевронов между кромками; у сектора — от 0,85 м, там тело моба.
        float lo=inner>0?inner+GTRimClear+.5*GTChevronDepth:.85;
        float hi=radius-GTRimClear-.5*GTChevronDepth;
        float rows=floor((hi-lo)/GTRowStep)+1;
        if(hi<lo)
        {
            // Узкое кольцо: один ряд посередине, если шеврон в него влезает.
            rows=radius-inner>=GTChevronDepth+.24?1:0;
            lo=.5*(inner+radius);
            hi=lo;
        }
        if(rows<1.5)
        {
            lo=.5*(lo+hi);
            hi=lo;
        }
        float rowStep=rows>1.5?(hi-lo)/(rows-1):1;
        float rk=lo+(rows>1.5?clamp(round((r-lo)/rowStep),0,rows-1)*rowStep:0);
        float n, ak;
        if(full)
        {
            // Кольцо: по всему кругу с шагом около GTChevronStep.
            n=max(3,floor(6.2831853*rk/GTChevronStep));
            float stepA=6.2831853/n;
            ak=round(a/stepA)*stepA;
        }
        else
        {
            // Сектор: шаг ровно GTChevronStep, ряд по центру, не ближе GTSideClear к лучам.
            float room=span*rk-(GTChevronWidth+2*GTSideClear);
            n=room>=0?floor(room/GTChevronStep)+1:0;
            float stepA=GTChevronStep/max(rk,1e-3);
            ak=(clamp(round(a/stepA+.5*(n-1)),0,max(n-1,0))-.5*(n-1))*stepA;
        }
        float da=a-ak;
        float lit=smoothstep(rk-.5*GTChevronDepth,rk+.5*GTChevronDepth,inner+p*fillLength);
        mark=GTChevronMark(GTChevron(float2(r*sin(da),r*cos(da)-rk)),px,lit);
        if(rows<.5||n<.5)mark.xy=0;
    }
    return GroundTelegraph(d,dash,saturate((r-inner)/fillLength),fillLength,mark,progress,opacity,exposed,flash);
}
#endif
