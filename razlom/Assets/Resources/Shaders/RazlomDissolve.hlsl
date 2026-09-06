#ifndef RAZLOM_DISSOLVE_INCLUDED
#define RAZLOM_DISSOLVE_INCLUDED

// РАСТВОРЕНИЕ ТЕЛА ПРИ СМЕРТИ.
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
    half _OutlineDepthBias;
    half _HitFlash;
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

// Расстояние до ближайшей точки-зерна. 27 соседей — как 9 у двумерного узла
// Unity. Урезать окрестность до 8 вдвое дешевле, но тогда ближайшее зерно
// иногда лежит снаружи просмотренного куба, и по маске идут прямые швы.
float RazlomDissolveVoronoi(float3 position)
{
    float3 cell = floor(position);
    float3 local = frac(position);
    float nearest = 8.0;

    for (int z = -1; z <= 1; z++)
    {
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                float3 lattice = float3(x, y, z);
                float3 feature = lattice + RazlomDissolveCellPoint(cell + lattice);
                nearest = min(nearest, distance(feature, local));
            }
        }
    }
    return nearest;
}

float RazlomDissolveValueHash(float3 cell)
{
    return frac(sin(dot(cell, float3(127.1, 311.7, 74.7))) * 43758.5453);
}

float RazlomDissolveValueNoise(float3 position)
{
    float3 cell = floor(position);
    float3 local = frac(position);
    float3 blend = local * local * (3.0 - 2.0 * local);

    float c000 = RazlomDissolveValueHash(cell + float3(0, 0, 0));
    float c100 = RazlomDissolveValueHash(cell + float3(1, 0, 0));
    float c010 = RazlomDissolveValueHash(cell + float3(0, 1, 0));
    float c110 = RazlomDissolveValueHash(cell + float3(1, 1, 0));
    float c001 = RazlomDissolveValueHash(cell + float3(0, 0, 1));
    float c101 = RazlomDissolveValueHash(cell + float3(1, 0, 1));
    float c011 = RazlomDissolveValueHash(cell + float3(0, 1, 1));
    float c111 = RazlomDissolveValueHash(cell + float3(1, 1, 1));

    float x00 = lerp(c000, c100, blend.x);
    float x10 = lerp(c010, c110, blend.x);
    float x01 = lerp(c001, c101, blend.x);
    float x11 = lerp(c011, c111, blend.x);
    return lerp(lerp(x00, x10, blend.y), lerp(x01, x11, blend.y), blend.z);
}

// Аналог Simple Noise из графа: три октавы value-шума.
float RazlomDissolveClouds(float3 position)
{
    return RazlomDissolveValueNoise(position) * 0.62
         + RazlomDissolveValueNoise(position * 2.17) * 0.27
         + RazlomDissolveValueNoise(position * 4.31) * 0.11;
}

// Что растворяется раньше: 0 — первым, 1 — последним.
float RazlomDissolveMask(float3 positionOS)
{
    float3 scaled = positionOS * _DissolveScale;

    // РАСТЯЖКА ПО ИЗМЕРЕННОМУ РАЗМАХУ, А НЕ ПО ТЕОРЕТИЧЕСКОМУ.
    //
    // Ни расстояние Вороного, ни сумма октав не занимают 0..1: они жмутся к
    // середине. Если гнать порог 0..1 по сырому значению, начало и конец
    // растворения — мёртвое время, когда на экране не меняется ничего.
    //
    // Границы сняты замером (40 000 точек по объёму тела 0.55 × 1.0 × 0.35,
    // масштабы 6/13/30 — перцентили 1 % и 99 %):
    //   Вороной  0.134 .. 0.90  →  (x - 0.13) * 1.30   (не зависит от масштаба)
    //   Облака   0.186 .. 0.72  →  (x - 0.19) * 1.86   (снято на 0.45 * 13)
    // Облака заметно уже Вороного и слегка ползут по масштабу: на
    // _DissolveScale ниже ~6 верх шкалы недобирает, и чисто облачный режим
    // дорастворяет тело раньше конца таймера. Значение по умолчанию — Вороной.
    float cells = saturate((RazlomDissolveVoronoi(scaled) - 0.13) * 1.30);
    float clouds = saturate((RazlomDissolveClouds(scaled * 0.45) - 0.19) * 1.86);
    float noise = lerp(clouds, cells, _DissolveVoronoi);

    // Свип по высоте — это Cutoff Height из графа. По умолчанию он выключен:
    // подробности в свойстве _DissolveHeightBias в RazlomTextureToon.shader,
    // коротко — «снизу вверх» читается как провал сквозь пол, а не как зола.
    float span = max(_DissolveHeightRange.y - _DissolveHeightRange.x, 0.001);
    float height = saturate((positionOS.y - _DissolveHeightRange.x) / span);
    return saturate(lerp(noise, height, _DissolveHeightBias));
}

// Выкусывает фрагмент, если он уже за фронтом растворения, и возвращает
// яркость самого фронта: 1 на кромке, 0 в глубине тела.
//
// Пока _DeathFade равен нулю, не считается вообще ничего. Свойство одинаково
// на весь draw call, так что ветка расходится не по пикселям, а по вызовам
// отрисовки: живые персонажи за 27 итераций Вороного не платят.
half RazlomDissolveFront(float3 positionOS)
{
    if (_DeathFade <= 0.0h) return 0.0h;

    float mask = RazlomDissolveMask(positionOS);

    // ПОРОГ ИДЁТ ПО КРИВОЙ, ПОТОМУ ЧТО МАСКА НЕ РАВНОМЕРНА.
    //
    // Доля видимого тела при _DeathFade 0.0 … 1.0, замер на тех же 40 000
    // точках:
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
    return front * front;
}

half3 RazlomDissolveEmber()
{
    return _DissolveEdgeColor.rgb * _DissolveEdgeGlow;
}

#endif
