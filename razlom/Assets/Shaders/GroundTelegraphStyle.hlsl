#ifndef RAZLOM_GROUND_TELEGRAPH_STYLE
#define RAZLOM_GROUND_TELEGRAPH_STYLE
// ОБЩИЙ СТИЛЬ МЕТОК НА ЗЕМЛЕ: «трещины со светом изнутри».
// Выбор владельца G6, 29.09: ART/characters/act-1-enemies/review/
// mobs-v2-concepts-2026-09-29/telegraphs/02-cracks-inner-light.png.
// Прежний стиль 25.09 (коралловая кромка + ровная заливка) заменён целиком.
//
// Что видно:
//  - земля внутри фигуры расходится плитами; сеть трещин привязана к миру
//    (ячейки Вороного по XZ), поэтому метка — окно в треснувший грунт, а не
//    наклейка, которая ездит за мобом;
//  - впереди заливки трещины закрыты: тонкие тёмно-багровые волоски;
//  - за бегущей кромкой трещины раскрываются на ~0,35 длины заливки и
//    светятся изнутри: красный край → оранжевый → жёлтая сердцевина,
//    плиты становятся красно-бурой коркой (темнее у трещин, светлее к
//    середине), свет подтекает на их края узким красным ореолом;
//  - по фронту заливки бежит яркая жёлто-оранжевая кромка;
//  - контур фигуры — тоже трещина: снаружи почти чёрная губа, в середине
//    горячий шов, внутри тонкая губа. Пара «тёмное + яркое» держит контраст
//    ≥3:1 с любой землёй: на тёмной вечерней траве работает свет, на
//    светлой — губа.
//
// ЦВЕТА ПОДОБРАНЫ В HDR, А НЕ «НА ГЛАЗ В sRGB». Метка смешивается в HDR-буфере,
// дальше CombatLook: экспозиция +0,4 EV, Neutral, блум (порог 1,05,
// интенсивность 0,22). Neutral давит яркое и обесцвечивает: оранжевый с
// зелёным 0,5 выходит кремовым. Поэтому русло и шов — красный 1,2–2,2 при
// зелёном ≤0,46, и цвета на выходе совпадают с реф-кадром (обратный ход через
// кривую Neutral от пикселей рефа). Прототип на numpy с этой же цепочкой на
// кадре Шипомёта (земля как есть, ×2 и ×3,5 по яркости): худшая кромка по
// всем фигурам и долям заливки — 3,15:1. Проверка в редакторе —
// GroundTelegraphSetup.RenderReview (кадры + замер по пикселям).
//
// Правила (утверждены вместе со старым стилем и не меняются):
//  - граница опасности считается в метрах от настоящей фигуры и ничем не
//    искажается: шум трогает только внутреннюю сторону кромки;
//  - один проход, без grab pass и без текстур: всё процедурно, поэтому стиль
//    подхватывает любой шейдер, который включает этот файл (сектор, полоса,
//    Вендиго, посадка плода) — без правки материалов;
//  - нет _Time: вид зависит только от заливки, которую вид считает по тику
//    Sim, поэтому пауза и повтор съёмки дают тот же кадр;
//  - за единицу выходит только красный канал (порог Bloom в CombatLook —
//    1.05, см. ArenaView): ореол трещин остаётся красно-оранжевым, а не жёлтым.

// Плита растрескавшейся земли, м: на реф-кадре на полосе 1,4 м помещается
// две-три плиты поперёк.
#define GT_CELL 0.55
// Разброс центров плит внутри ячейки: 0 — сетка, 1 — полный хаос.
// 0,70 — не больше: при нём второй обход 3×3 даёт то же расстояние до
// трещины, что и точный 5×5 (замер на 15×15 м с шагом 1 см: расхождение
// в 2 точках из 2,25 млн, до 5 мм). При 0,76 — уже 113 точек до 2,7 см.
#define GT_JITTER 0.70

// Хэш без синуса (Hoskins): одинаков на всех GPU и не плывёт на больших координатах.
float2 GtHash22(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Сеть трещин. .x — расстояние до ближайшей трещины (ребра Вороного), м;
// .y — случайное число плиты 0..1 (оттенок плиты).
// Точное расстояние до ребра: ближайший центр, потом проекция на
// серединные перпендикуляры соседей вокруг найденной ячейки (3×3, см. GT_JITTER).
float2 GtCrackField(float2 worldXZ)
{
    float2 x = worldXZ / GT_CELL;
    // Лёгкий изгиб и мелкий излом: рёбра не должны быть линейкой.
    x += .07 * float2(sin(x.y * 2.1 + 1.3 * sin(x.x * 1.7)), sin(x.x * 2.3 + 1.1 * sin(x.y * 1.9)))
       + .03 * float2(sin(x.y * 9.7 + x.x * 3.1), sin(x.x * 10.3 - x.y * 2.9));
    float2 n = floor(x), f = x - n;
    const float lo = .5 - .5 * GT_JITTER;
    float2 mg = 0, mr = 0;
    float md = 8;
    [unroll] for (int j = -1; j <= 1; j++)
    {
        [unroll] for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(i, j);
            float2 r = g + lo + GT_JITTER * GtHash22(n + g) - f;
            float d = dot(r, r);
            if (d < md) { md = d; mr = r; mg = g; }
        }
    }
    md = 8;
    [unroll] for (int b = -1; b <= 1; b++)
    {
        [unroll] for (int a = -1; a <= 1; a++)
        {
            float2 g = mg + float2(a, b);
            float2 r = g + lo + GT_JITTER * GtHash22(n + g) - f;
            float2 dr = r - mr;
            float l2 = dot(dr, dr);
            if (l2 > 1e-5) md = min(md, dot(.5 * (mr + r), dr * rsqrt(l2)));
        }
    }
    return float2(md * GT_CELL, GtHash22(n + mg).x);
}

// Слой поверх накопленного (премультиплицированный «over»).
void GtOver(inout float3 premul, inout float alpha, float3 color, float a)
{
    a = saturate(a);
    premul = color * a + premul * (1 - a);
    alpha = a + alpha * (1 - a);
}

// Линейная ступень на ширину пикселя: кромки ровно в пиксель, без мыла.
float GtRamp(float a, float b, float x) { return saturate((x - a) / max(b - a, 1e-6)); }

// distanceInside  — метры до границы фигуры (>0 внутри);
// fillCoordinate  — 0..1 вдоль заливки (от моба/центра к краю);
// fillMetres      — сколько метров проходит заливка от 0 до 1;
// progress        — доля до удара по тикам Sim; opacity — видимость;
// exposedBoundary — 0 там, где кромку закрывает соседняя фигура (посадка плода).
half4 GroundTelegraphCracks(float2 worldXZ, float distanceInside, float fillCoordinate, float fillMetres,
    float progress, float opacity, float exposedBoundary)
{
    // Метры в пикселе: по земле в среднем (ортокамера сжимает глубину) и
    // поперёк кромки — по ней считаются толщины линий, чтобы на 1080p ни одна
    // не стала тоньше пикселя и не мерцала.
    float px = max(.5 * (length(ddx(worldXZ)) + length(ddy(worldXZ))), 1e-4);
    float pxD = max(length(float2(ddx(distanceInside), ddy(distanceInside))), 1e-4);
    float h = .5 * pxD;
    float inside = GtRamp(-h, h, distanceInside);

    float p = saturate(progress);
    float fm = max(fillMetres, .05);
    // Метры до фронта: плюс — впереди (ещё не залито), минус — позади.
    float front = (fillCoordinate - p) * fm;
    float hF = .5 * max(length(float2(ddx(fillCoordinate), ddy(fillCoordinate))) * fm, 1e-4);
    float started = saturate(p * 50);
    float filled = (1 - GtRamp(-hF, hF, front)) * started;
    // Трещина раскрывается не сразу за фронтом, а на отрезке позади него.
    float opened = filled * smoothstep(0, 1, saturate(-front / clamp(.35 * fm, .15, .7)));
    // Жар у самого фронта: свежие трещины ярче.
    float heat = filled * exp(-max(-front, 0) / .45);
    float urgency = smoothstep(.75, 1, p);

    // ---- трещины внутри
    float2 crack = GtCrackField(worldXZ);
    float e = crack.x;
    float wob = .5 + .5 * sin(worldXZ.x * 3.7 + 2.1 * sin(worldXZ.y * 2.3)) * sin(worldXZ.y * 4.1 - 1.7 * sin(worldXZ.x * 1.9));
    // Полуширина: закрытая — волосок в полпикселя, открытая — 2,6–6 см
    // (на реф-кадре раскрытая трещина — русло в 5–8 см).
    float w = lerp(max(.005, .5 * px), max(.026 + .034 * wob, .9 * px), opened);
    float hp = .5 * px;
    float core = 1 - GtRamp(w - hp, w + hp, e);
    float lipEdge = w + .005 + .008 * opened + .6 * px;
    float lip = 1 - GtRamp(lipEdge - hp, lipEdge + hp, e);
    float halo = exp(-max(e - w, 0) / (.008 + .015 * opened + .010 * heat));

    float3 premul = 0;
    float alpha = 0;
    // Плиты: чуть тронуты впереди, обожжённая красно-бурая корка позади
    // фронта. У трещины плита темнее, к середине светлее — приподнятая корка,
    // как на реф-кадре, а не ровная заливка. Цвета подобраны обратным ходом
    // через тонмаппинг: плита на выходе ≈ (109, 56, 26) в sRGB, как на рефе.
    float bevel = smoothstep(0, 1, saturate((e - w) / .16)) * (.75 + .5 * crack.y);
    float3 plate = lerp(float3(.040, .011, .004), float3(.110, .030, .008), min(bevel, 1.2));
    GtOver(premul, alpha, plate, inside * (.10 + .62 * filled) * (.85 + .3 * crack.y));
    // Свет из трещины на краях плит: узкий красный ореол. Жёлтым он быть не
    // должен — после Neutral жёлтый ореол на траве выходит кремовым пятном.
    GtOver(premul, alpha, float3(1, .10, .012), halo * inside * filled * (.55 + .12 * urgency + .10 * heat));
    GtOver(premul, alpha, float3(.03, .008, .004), lip * inside * opened * .30);
    // Русло: красный край → оранжевый → жёлтая сердцевина. За единицу выходит
    // только красный: так после экспозиции и Neutral русло остаётся
    // насыщенным (на рефе край ≈ (244, 53, 17), сердцевина ≈ (254, 199, 59)),
    // а не выцветает в кремовый, как при зелёном за 0,5.
    float t = saturate(1 - e / max(w, 1e-4));
    float3 lava = lerp(float3(1.20, .03, .006), float3(1.80, .10, .008), saturate(t * 2));
    float t2 = saturate(t * 2 - 1);
    lava = lerp(lava, float3(2.20, .46, .02), t2 * t2);
    lava *= 1 + .10 * urgency + .08 * heat;
    lava.gb = min(lava.gb, .98);
    GtOver(premul, alpha, lerp(float3(.30, .020, .008), lava, filled), core * inside * (.55 + .42 * filled));

    // ---- кромка фигуры: тёмная губа | горячая середина | тонкая губа
    float wob2 = .5 + .5 * sin(worldXZ.x * 5.3 + 1.7 * sin(worldXZ.y * 3.1)) * sin(worldXZ.y * 5.9 - 1.3 * sin(worldXZ.x * 2.7));
    // Тёмная губа — почти чёрная и непрозрачная: на средне-светлой земле
    // (яркость 0,12–0,2) яркая сердцевина после Neutral до 3:1 не дотягивает,
    // и кромку держит только губа. 3 см, чтобы блум соседней сердцевины не
    // высветлял её целиком.
    float outer = max(.030, 2.4 * pxD);
    float bcore = max(.030, 2.3 * pxD) * (1 + (wob2 - .5) * .5 * filled) + .018 * opened;
    float bandEnd = outer + bcore + max(.014, pxD);
    float band = (1 - GtRamp(bandEnd - h, bandEnd + h, distanceInside)) * inside;
    float bc = GtRamp(outer - h, outer + h, distanceInside) * (1 - GtRamp(outer + bcore - h, outer + bcore + h, distanceInside));
    float bhalo = exp(-max(distanceInside - outer - bcore, 0) / .06) * inside;
    GtOver(premul, alpha, float3(1, .10, .012), bhalo * exposedBoundary * filled * (.30 + .12 * urgency));
    GtOver(premul, alpha, float3(.006, .002, .0015), band * exposedBoundary);
    // Середина кромки — жёлто-горячий шов с красными боками, как русло
    // трещины. Шов нужен ради яркости (зелёный ≈ 0,4 даёт светлоту), бока —
    // ради цвета опасности. Плоская вершина профиля: хотя бы один пиксель
    // кромки — полного цвета.
    float tb = smoothstep(0, 1, saturate(2.0 * (1 - abs(distanceInside - outer - bcore * .5) / (bcore * .5))));
    float3 edgeAhead = lerp(float3(1.10, .05, .010), float3(2.00, .40, .020), tb);
    float3 edgeBehind = lerp(float3(1.50, .10, .010), float3(2.20, .46, .020), tb);
    GtOver(premul, alpha, lerp(edgeAhead, edgeBehind, filled), bc * exposedBoundary * .96);

    // ---- бегущая кромка заливки (нет в начале и после удара)
    float gate = started * saturate((1 - p) * 50);
    float rimW = max(.032, 2.2 * px);
    float rim = 1 - GtRamp(rimW * .5 - hF, rimW * .5 + hF, abs(front + rimW * .5));
    GtOver(premul, alpha, float3(1, .10, .012), exp(-max(-front, 0) / .20) * filled * inside * gate * .34);
    GtOver(premul, alpha, float3(2.20, .44, .020), rim * inside * gate * .96);

    return half4(premul / max(alpha, 1e-4), alpha * saturate(opacity));
}

// Старый вход (25.09) для шейдеров, которые не знают длину заливки в метрах
// (Razlom/Forest Bud Landing). Длина берётся из производных: |∇d| / |∇fill|.
// Точна, когда заливка идёт поперёк кромки (круг: заливка по радиусу).
// directionMark больше не рисуется: направление читается по бегущей кромке.
half4 GroundTelegraph(float2 worldXZ, float distanceInside, float fillCoordinate,
    float progress, float opacity, float exposedBoundary, float directionMark)
{
    float gF = length(float2(ddx(fillCoordinate), ddy(fillCoordinate)));
    float gD = length(float2(ddx(distanceInside), ddy(distanceInside)));
    return GroundTelegraphCracks(worldXZ, distanceInside, fillCoordinate, gD / max(gF, 1e-6),
        progress, opacity, exposedBoundary);
}
#endif
