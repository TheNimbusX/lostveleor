#ifndef RAZLOM_DISSOLVE_INCLUDED
#define RAZLOM_DISSOLVE_INCLUDED

// РАСПАД ТЕЛА ПРИ СМЕРТИ — КУСКАМИ, А НЕ ПИКСЕЛЬНЫМ ШУМОМ (поток I, 29.09).
//
// Вороной с порогом по расстоянию до зерна выедал тело мелкой рябью: на экране
// это читалось «растворился в шуме», а владелец просит «рассыпался в кору,
// листья, споры». Теперь маска считается ПО КУСКАМ:
//   * в первые кадры распада по всему телу проступают трещины — швы между
//     кусками (F2 − F1 мало), тёплые, потом цвета сердцевины;
//   * у каждого куска (клетки Вороного) своя очередь — хеш номера клетки;
//     кусок уходит коротко, от своих краёв к середине;
//   * кромка фронта и трещины красятся цветом излома (_DissolveEdgeColor —
//     сердцевина коры, труха, кость), а не только углями: скол выглядит
//     материалом.
// Порог — тот же _DeathFade, свойства и UnityPerMaterial не менялись (SRP
// batcher), _DissolveVoronoi больше не читается.
//
// _DissolveScale теперь — кусков на ОБЪЕКТНУЮ единицу; ArenaView ставит его
// каждому телу в момент смерти из размера куска вида в метрах и масштаба
// рендерера (EnemyKillBeat.ChunkMetres), так что куски у мелкого корнеполза и
// у вендиго одного «материального» размера, а не одной доли тела.
//
// Ниже — прежняя история маски: почему объектное пространство и почему кривая
// порога. Она по-прежнему в силе.
//
// Здесь стоял дизеринг по экранным пикселям: порог _DeathFade сравнивался с
// interleaved gradient noise от positionCS.xy. Он честно выключал пиксели, но
// узор жил в ЭКРАНЕ, а не в теле — при повороте камеры сетка стояла на месте,
// у всех врагов она была одна и та же, и это читалось как артефакт
// прозрачности, а не как эффект. Никакого края у растворения не было вовсе.
//
// Маска взята из Shadergraph'а «Resources/Shaders/Dissolve Shader»: Voronoi
// даёт край из клеток, value-шум — облачный; там между ними стоял Branch, тут
// это плавный blend (_DissolveVoronoi). Порог остался ОДИН И ТОТ ЖЕ
// _DeathFade, поэтому со стороны C# не поменялось ничего: ArenaView гонит его
// по таймеру смерти ровно как раньше.
//
// Шум считается в ОБЪЕКТНОМ пространстве, а не в UV и не в мире:
//   * UV разорваны по швам атласа — растворение шло бы кусками вдоль швов;
//   * мир не годится, потому что тело смерти подбрасывает и крутит
//     (DeathSpinSpeed 520°/с за 0.30 с — это полтора оборота), и узор
//     проезжал бы сквозь тело, как будто оно летит через неподвижную решётку.
// В объектном пространстве узор приклеен к телу и крутится вместе с ним.
// Для SkinnedMeshRenderer POSITION приходит уже после скиннинга, так что узор
// слегка плывёт по анимации — за треть секунды этого не видно.

CBUFFER_START(UnityPerMaterial)
    // ОДНО ОПРЕДЕЛЕНИЕ НА ВСЕ ЧЕТЫРЕ ПРОХОДА.
    //
    // Раньше этот блок был скопирован в каждый Pass. SRP batcher требует,
    // чтобы UnityPerMaterial совпадал побайтово во всех проходах шейдера:
    // достаточно было добавить свойство в три копии из четырёх, и батчинг
    // молча отваливался — без ошибки, только с просевшим кадром.
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _ShadowColor;
    half4 _MidColor;
    half _MidThreshold;
    half _LightThreshold;
    half _LightFeather;
    half4 _RimColor;
    half _RimPower;
    half4 _OutlineColor;
    half _OutlineWidth;
    half _WhiteClothLift;
    half _ArtSaturation;
    half _ArtContrast;
    half _SkinToneStrength;
    half _SkinSaturation;
    float4 _SkinToneScale;
    half _OutlineDepthBias;
    half _HitFlash;
    half _BlazeGlow;
    half _DeathFade;
    half4 _DissolveEdgeColor;
    half _DissolveEdgeGlow;
    half _DissolveEdgeWidth;
    float _DissolveScale;
    half _DissolveVoronoi;
    half _DissolveHeightBias;
    float4 _DissolveHeightRange;
CBUFFER_END

// Хеши считаются во float намеренно. В half синус большого аргумента
// вырождается: соседние клетки получают один и тот же «случайный» вектор, и
// вместо клеток по телу идут полосы.
float3 RazlomDissolveCellPoint(float3 cell)
{
    // Узел Voronoi у Unity гоняет хеш ещё раз через sin/cos с Angle Offset —
    // это ручка для АНИМАЦИИ узора, которая нам не нужна: узор живёт треть
    // секунды. Выкинув её, мы экономим половину трансцендентных операций из
    // 27 итераций ниже, а распределение остаётся тем же по построению
    // (точка равномерна в единичной ячейке).
    float3 hash = float3(dot(cell, float3(127.1, 311.7, 74.7)),
                         dot(cell, float3(269.5, 183.3, 246.1)),
                         dot(cell, float3(113.5, 271.9, 124.6)));
    return frac(sin(hash) * 43758.5453);
}

float RazlomDissolveValueHash(float3 cell)
{
    return frac(sin(dot(cell, float3(127.1, 311.7, 74.7))) * 43758.5453);
}

// Куски Вороного: F1 и F2 (расстояния до ближайшего и второго зерна) и
// номер клетки ближайшего зерна. 27 соседей — как 9 у двумерного узла Unity:
// урежь окрестность до 8, и ближайшее зерно иногда лежит снаружи
// просмотренного куба — по маске идут прямые швы.
void RazlomDissolveChunks(float3 position, out float f1, out float f2, out float3 owner)
{
    float3 cell = floor(position);
    float3 local = frac(position);
    f1 = 8.0;
    f2 = 8.0;
    owner = cell;

    for (int z = -1; z <= 1; z++)
    {
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                float3 lattice = float3(x, y, z);
                float3 feature = lattice + RazlomDissolveCellPoint(cell + lattice);
                float d = distance(feature, local);
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                    owner = cell + lattice;
                }
                else
                {
                    f2 = min(f2, d);
                }
            }
        }
    }
}

// Доля маски, отданная шву внутри куска. Остальное — очередь куска.
// Больше — кусок дольше крошится от краёв (ближе к старой ряби), меньше —
// отваливается почти целиком. 0.24: каждый кусок живёт от первой щербины на
// краю до исчезновения около четверти таймера — уходит куском, а не рябью.
#define RAZLOM_CRACK_SHARE 0.24
// На каком F2 − F1 шов считается закончившимся, в долях клетки.
#define RAZLOM_CRACK_DEPTH 0.42
// ТРЕЩИНЫ ПО ВСЕМУ ТЕЛУ СРАЗУ. Очередь кусков случайна, поэтому сам фронт
// проходит тело кусок за куском. Чтобы тело сначала ЛОПНУЛО, а потом
// осыпалось (кадр 01: «кора трескается светом»), швы между всеми кусками
// проступают цветом излома за первые 1/RAZLOM_CRACK_RISE распада — до того,
// как уйдёт большинство кусков. Ширина — по F2 − F1 в долях клетки: при куске
// 0,15 м это 1–2 см, пара пикселей на камере боя.
#define RAZLOM_CRACK_RISE 8.0
#define RAZLOM_CRACK_LINE_IN 0.05
#define RAZLOM_CRACK_LINE_OUT 0.14

// Что уходит раньше: 0 — первым, 1 — последним. gap — F2 − F1 в долях
// клетки: 0 на шве между кусками.
float RazlomDissolveMask(float3 positionOS, out float gap)
{
    float3 scaled = positionOS * _DissolveScale;

    float f1, f2;
    float3 owner;
    RazlomDissolveChunks(scaled, f1, f2, owner);
    gap = f2 - f1;

    // Очередь куска — хеш его клетки: равномерна в 0..1 по построению, поэтому
    // растяжки по замеру (как у расстояния Вороного) здесь не нужно.
    float order = RazlomDissolveValueHash(owner + 17.0);
    // Шов: 0 на границе двух кусков, 1 в глубине куска.
    float seam = saturate(gap / RAZLOM_CRACK_DEPTH);
    float noise = order * (1.0 - RAZLOM_CRACK_SHARE) + seam * RAZLOM_CRACK_SHARE;

    // Свип по высоте — это Cutoff Height из графа. По умолчанию он выключен:
    // подробности в свойстве _DissolveHeightBias в RazlomTextureToon.shader,
    // коротко — «снизу вверх» читается как провал сквозь пол, а не как зола.
    float span = max(_DissolveHeightRange.y - _DissolveHeightRange.x, 0.001);
    float height = saturate((positionOS.y - _DissolveHeightRange.x) / span);
    return saturate(lerp(noise, height, _DissolveHeightBias));
}

// Выкусывает фрагмент, если он уже за фронтом растворения, и возвращает
// яркость излома: 1 на кромке фронта и на трещинах между кусками, 0 в
// глубине тела.
//
// Пока _DeathFade равен нулю, не считается вообще ничего. Свойство одинаково
// на весь draw call, так что ветка расходится не по пикселям, а по вызовам
// отрисовки: живые персонажи за 27 итераций Вороного не платят.
half RazlomDissolveFront(float3 positionOS)
{
    if (_DeathFade <= 0.0h) return 0.0h;

    float gap;
    float mask = RazlomDissolveMask(positionOS, gap);

    // ПОРОГ ИДЁТ ПО КРИВОЙ. Маска кусков почти равномерна (очередь — хеш), и
    // показатель 0.9 лишь чуть сдвигает работу к началу: первые трещины
    // проступают в тот же кадр, что и залп обломков. Замер ниже снят ещё на
    // старой маске Вороного — он объясняет, откуда кривая взялась.
    //
    // Доля видимого тела при _DeathFade 0.0 … 1.0, замер на 40 000
    // точках (старая маска):
    //   свип 0,    порог линейный: 100 96 91 81 69 53 37 22 11  4 0
    //   свип 0,    порог ^0.9:     100 95 88 77 63 47 32 18  9  4 0
    //   свип 0.35, порог линейный: 100 99 96 87 72 52 32 15  5  1 0
    //   свип 0.35, порог ^0.9:     100 99 94 82 65 45 26 11  4  1 0
    // Со свипом линейный порог убирает за первую треть таймера всего 13 %
    // тела — 100 мс из 300, на которых на экране не происходит ничего.
    // Показатель 0.9 сдвигает работу к началу и остаётся честным при
    // выключенном свипе, где маска и так почти равномерна.
    //
    // Множитель 1.002 и сдвиг 0.001 — гарантия краёв: при _DeathFade 0 не
    // выкусывается ни один фрагмент, при 1 не остаётся ни одного.
    // saturate, а не голый _DeathFade: pow от отрицательного основания даёт
    // NaN, а NaN в clip выкусывает фрагмент через раз. Свойство объявлено
    // Range(0,1), но приходит оно из MaterialPropertyBlock, а тот диапазон
    // свойства не соблюдает.
    float threshold = pow(saturate(_DeathFade), 0.9) * 1.002 - 0.001;
    float ahead = mask - threshold;
    clip(ahead);

    // Квадрат сужает свечение к самой кромке: линейный спад на всю
    // _DissolveEdgeWidth выглядит не углями по краю, а подсветкой изнутри.
    half front = 1.0h - (half)saturate(ahead / max(_DissolveEdgeWidth, 0.001h));
    // Трещина между кусками — та же кромка излома, только по всему телу.
    half crack = (half)(saturate(_DeathFade * RAZLOM_CRACK_RISE)
        * (1.0 - smoothstep(RAZLOM_CRACK_LINE_IN, RAZLOM_CRACK_LINE_OUT, gap)));
    return max(front * front, crack);
}

half3 RazlomDissolveEmber()
{
    return _DissolveEdgeColor.rgb * _DissolveEdgeGlow;
}

// ЦВЕТ ИЗЛОМА. Кромка фронта и трещины — сердцевина материала
// (_DissolveEdgeColor) под тем же светом, что и тело (light — множитель
// освещения альбедо), и поверх — угли _DissolveEdgeGlow. В первые ~0,1 с
// распада (и пока гаснет вспышка добивания) трещины светятся тёплым: в целевом
// кадре 01 кора лопается светом, а через кадр-другой шов — уже просто
// сердцевина дерева, кости, трухи. Высветления тела здесь нет: красятся только
// узкие швы, и только на трупе.
half3 RazlomDissolveBreak(half3 color, half3 light, half front)
{
    if (front <= 0.0h) return color;
    half3 inner = _DissolveEdgeColor.rgb * light;
    half warm = max(saturate(_HitFlash), saturate(1.0h - _DeathFade * 4.0h) * 0.75h);
    half3 seam = lerp(inner, half3(1.0h, 0.70h, 0.34h), warm);
    return lerp(color, seam, front * 0.9h) + RazlomDissolveEmber() * front;
}

#endif
