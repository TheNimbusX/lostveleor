#ifndef RAZLOM_FOLIAGE_WIND_INCLUDED
#define RAZLOM_FOLIAGE_WIND_INCLUDED

// ВЕТЕР В ЛИСТВЕ — ЭТО ВЕРШИННЫЙ СДВИГ, А НЕ КОСТИ И НЕ ФИЗИКА.
//
// Дерево — статичный проп. Ригом качать его нечестно дорого: скиннинг, Animator
// и клип на каждый экземпляр ради движения, которое целиком считается из
// позиции вершины. Здесь всё живёт в вершинном шейдере: несколько синусов и два
// поворота на вершину, ноль компонентов в сцене, ноль аллокаций в кадре.
//
// ══════════════════════════════════════════════════════════════════════════
// ГЛАВНОЕ ПРАВИЛО: ВСЁ ВРАЩАЕТСЯ, НИЧТО НЕ СДВИГАЕТСЯ.
//
// Вторая версия двигала вершины ТРАНСЛЯЦИЕЙ: крона ехала вбок, амплитуда росла
// с высотой. Это деформация сдвига — расстояния между вершинами меняются, форма
// кроны плывёт, и на экране это ровно желе. Никакой амплитудой это не лечится:
// маленькое желе — это просто маленькое желе.
//
// Настоящая ветка не съезжает вбок, она ПОВОРАЧИВАЕТСЯ вокруг места крепления.
// Поворот сохраняет все расстояния: форма остаётся той же, меняется только
// наклон. Поэтому здесь оба слоя движения — повороты:
//
//   1. Наклон  — всё дерево поворачивается вокруг основания ствола, угол
//                нарастает с высотой (это и есть изгиб ствола).
//   2. Трепет  — каждая листовая шапка поворачивается вокруг СВОЕГО центра,
//                вокруг своей случайной оси. Шапка не раздувается и не
//                проседает, она дёргается на месте.
//
// Побочная выгода: поворот сам опускает кончик ветки при наклоне, и костыль
// «проседание при наклоне» из прошлой версии выброшен за ненадобностью.
// ══════════════════════════════════════════════════════════════════════════
//
// ПОЧЕМУ ФАЗА — ХЕШ, А НЕ dot(позиция, вектор).
//
// В первой версии стояло sin(ωt + dot(positionWS, k)). Фаза, линейная по
// координате, — это в точности плоская бегущая волна: по кроне шёл ровный
// синусоидальный фронт. Нужна псевдослучайная фаза, тогда крона мерцает, а не
// колышется фронтом.
//
// Хеш берётся ПО КЛЕТКЕ, а не по вершине: по вершине развело бы фазы внутри
// одного листа и его бы порвало. Клетка — куб размером с листовую шапку, все
// её вершины ходят вместе. Точный способ — покрасить листовые острова в
// вершинный цвет в Blender; клетка это приближение для модели без крашеных
// данных.
//
// ФАЗА ДЕРЕВА БЕРЁТСЯ ИЗ МАТРИЦЫ ОБЪЕКТА, А НЕ ИЗ СВОЙСТВА МАТЕРИАЛА.
// Свойство одинаково на весь draw call, и лес из одного материала закачался бы
// синхронно, как кордебалет. Позиция пивота приходит из UNITY_MATRIX_M, то есть
// переживает и SRP Batcher, и GPU instancing.

CBUFFER_START(UnityPerMaterial)
    // ОДНО ОПРЕДЕЛЕНИЕ НА ВСЕ ТРИ ПРОХОДА — ровно по той же причине, что и в
    // RazlomDissolve.hlsl: SRP batcher требует побайтового совпадения
    // UnityPerMaterial во всех проходах шейдера, и разошедшаяся копия отключает
    // батчинг молча, без единой ошибки в консоли.
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _ShadowColor;
    half4 _MidColor;
    half _MidThreshold;
    half _LightThreshold;
    half _LightFeather;
    half4 _RimColor;
    half _RimPower;
    // x — объектный Y, до которого ствол стоит намертво (он же ось поворота),
    // y — объектный Y верхушки,
    // zw — объектные X и Z центра ствола, вокруг которого идёт наклон.
    // Всё это меряется по мешу: Editor/RazlomFoliageSetup.cs.
    float4 _WindHeightRange;
    float _WindSwayAngle;
    float _WindSwayFrequency;
    float _WindFlutterAngle;
    float _WindFlutterFrequency;
    half _WindGust;
    float _WindClusterSize;
    half _WindBranchScatter;
CBUFFER_END

// Глобальная, ставится компонентом FoliageWindZone. Объявлена ВНЕ
// UnityPerMaterial намеренно: она принадлежит сцене, а не материалу, и попав в
// материальный буфер сломала бы батчинг так же, как разошедшаяся копия.
// xyz — направление в мире, w — сила.
float4 _RazlomWindDirection;

// Хеш во float, а не в half, — как и в RazlomDissolve.hlsl: в half синус
// большого аргумента вырождается, соседние клетки получают одно и то же
// «случайное» число, и вместо разброса по кроне снова идут полосы.
float RazlomFoliageHash(float3 cell)
{
    return frac(sin(dot(cell, float3(127.1, 311.7, 74.7))) * 43758.5453);
}

// Поворот вектора вокруг произвольной оси, формула Родрига. Одна пара
// sincos на поворот — дешевле, чем строить матрицу.
float3 RazlomRotateAroundAxis(float3 v, float3 axis, float angle)
{
    float sinA, cosA;
    sincos(angle, sinA, cosA);
    return v * cosA + cross(axis, v) * sinA + axis * (dot(axis, v) * (1.0 - cosA));
}

// Насколько вершина участвует в качании: 0 у корней, 1 в кроне.
//
// КВАДРАТ, А НЕ ЛИНЕЙНАЯ ДОЛЯ. У линейной маски производная на нижней границе
// не равна нулю, и переход от неподвижного ствола к качающейся ветке читается
// как излом — в одном кольце вершин движение уже есть, в соседнем ещё нет.
// Квадрат сажает и значение, и наклон в ноль одновременно, а заодно даёт
// профиль изгиба сужающегося ствола: низ почти жёсткий, верхушка гуляет.
float RazlomFoliageMask(float3 positionOS)
{
    float low = _WindHeightRange.x;
    float high = max(_WindHeightRange.y, low + 0.0001);
    float height = saturate((positionOS.y - low) / (high - low));
    return height * height;
}

// Новая ОБЪЕКТНАЯ позиция вершины. Всё считается в объектном пространстве, а в
// мировое уходит одним преобразованием в конце: повороты вокруг основания
// ствола и вокруг центра шапки естественно живут именно здесь.
float3 RazlomFoliageWindPosition(float3 positionOS)
{
    float mask = RazlomFoliageMask(positionOS);

    // Ветка расходится не по пикселям, а по вершинам ствола, и ни синусов, ни
    // поворотов для них не считается вовсе.
    if (mask <= 0.0) return positionOS;

    float time = _TimeParameters.x;

    // БЕЗ ЗОНЫ В СЦЕНЕ ГЛОБАЛ РАВЕН НУЛЮ.
    //
    // Материал, который «не работает, пока не поставишь ещё один компонент»,
    // читается как сломанный. Поэтому нулевой глобал — это «зоны нет», и тогда
    // дует слабый ветер по умолчанию. Выключить ветер по-прежнему можно:
    // FoliageWindZone на OnDisable пишет ВАЛИДНОЕ направление с нулевой силой.
    float2 flatDir = _RazlomWindDirection.xz;
    float power = _RazlomWindDirection.w;
    if (dot(flatDir, flatDir) < 1e-6)
    {
        flatDir = float2(0.7071, 0.7071);
        power = 1.0;
    }
    flatDir = normalize(flatDir);
    float3 windWS = float3(flatDir.x, 0.0, flatDir.y);

    float4x4 objectToWorld = GetObjectToWorldMatrix();
    float3 pivotWS = float3(objectToWorld._m03, objectToWorld._m13, objectToWorld._m23);
    float treePhase = dot(pivotWS.xz, float2(0.71, 1.37));

    // Клетка размером с листовую шапку и три НЕЗАВИСИМЫХ хеша от неё: фаза
    // наклона ветки, фаза и частота трепета, ось трепета. Один хеш на всё
    // связал бы слои: шапки, качнувшиеся вместе, вместе бы и дёргались.
    float cluster = max(_WindClusterSize, 0.01);
    float3 cell = floor(positionOS / cluster);
    float branchRandom = RazlomFoliageHash(cell);
    float leafRandom = RazlomFoliageHash(cell + 17.31);
    float axisRandom = RazlomFoliageHash(cell + 41.77);

    // Порыв — произведение двух синусов на несоизмеримых частотах. Один синус
    // дал бы ровное дыхание с ясным периодом; у произведения периода на глаз
    // нет, и большую часть времени оно держится около середины.
    float gust = 0.5 + 0.5 * sin(time * 0.19 + treePhase * 0.5)
                           * sin(time * 0.31 + treePhase * 0.23);
    power *= lerp(1.0, gust, _WindGust);

    // ── 1. ТРЕПЕТ: шапка поворачивается вокруг СВОЕГО центра ──
    //
    // Именно поворот, а не сдвиг вдоль нормали, как было раньше. Сдвиг вдоль
    // нормали раздувает и сдувает шапку — это пульсация, то есть то же желе,
    // только мелкое. Поворот вокруг центра сохраняет форму шапки полностью:
    // она дёргается на месте, как настоящая листва на ветке.
    //
    // Ось у каждой шапки своя и случайная: общая ось дала бы всей кроне один
    // ритм и вернула бы когерентность, от которой мы уходили.
    float flutterFrequency = _WindFlutterFrequency * (0.75 + 0.5 * leafRandom);
    float flutterAngle = sin(time * flutterFrequency * 6.2831853
                             + leafRandom * 6.2831853)
                       * radians(_WindFlutterAngle) * mask * power;

    float3 clusterCenter = (cell + 0.5) * cluster;
    float3 leafAxis = normalize(float3(leafRandom - 0.5,
                                       axisRandom - 0.5,
                                       branchRandom - 0.5) + 0.001);
    positionOS = clusterCenter +
        RazlomRotateAroundAxis(positionOS - clusterCenter, leafAxis, flutterAngle);

    // ── 2. НАКЛОН: всё дерево поворачивается вокруг основания ствола ──
    //
    // Ось поворота горизонтальна и перпендикулярна ветру, поэтому крона
    // кланяется ПО ветру. Угол растёт с маской — это изгиб ствола, а не
    // жёсткий поворот всей модели.
    float3 windOS = TransformWorldToObjectDir(windWS);
    windOS.y = 0.0;
    // Дерево, положенное набок в сцене, дало бы вырожденный горизонтальный
    // вектор. Такой случай не запрещаем, просто не делим на ноль.
    windOS = dot(windOS, windOS) > 1e-6 ? normalize(windOS) : float3(1.0, 0.0, 0.0);
    float3 swayAxis = normalize(cross(float3(0.0, 1.0, 0.0), windOS));

    float swayAngle = sin(time * _WindSwayFrequency * 6.2831853
                          + treePhase + branchRandom * 6.2831853 * _WindBranchScatter)
                    * radians(_WindSwayAngle) * mask * power;

    float3 trunkBase = float3(_WindHeightRange.z, _WindHeightRange.x, _WindHeightRange.w);
    positionOS = trunkBase +
        RazlomRotateAroundAxis(positionOS - trunkBase, swayAxis, swayAngle);

    return positionOS;
}

// Полный набор координат вершины из УЖЕ СДВИНУТОЙ мировой позиции.
//
// Штатный GetVertexPositionInputs считает всё от исходной объектной позиции и
// про наш поворот не знает. Собрать структуру руками нужно затем, чтобы
// GetShadowCoord получил ту же вершину, что ушла в растеризацию: иначе тень на
// самой кроне поедет относительно её собственной геометрии.
VertexPositionInputs RazlomFoliagePositionInputs(float3 positionWS)
{
    VertexPositionInputs inputs;
    inputs.positionWS = positionWS;
    inputs.positionVS = TransformWorldToView(positionWS);
    inputs.positionCS = TransformWorldToHClip(positionWS);

    float4 ndc = inputs.positionCS * 0.5;
    inputs.positionNDC.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
    inputs.positionNDC.zw = inputs.positionCS.zw;
    return inputs;
}

// Одна строчка на все проходы. Проходы обязаны звать именно её и ничего своего
// не изобретать: расхождение между цветом, тенью и глубиной ловится потом
// неделю.
float3 RazlomFoliageWorldPosition(float3 positionOS)
{
    return TransformObjectToWorld(RazlomFoliageWindPosition(positionOS));
}

#endif
