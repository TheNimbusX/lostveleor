#ifndef RAZLOM_GLOW_SHELL_INCLUDED
#define RAZLOM_GLOW_SHELL_INCLUDED

// НАРУЖНОЕ СВЕЧЕНИЕ ВРАГА.
//
// Задача с референса владельца: враг отделён от земли тёплым ореолом, который
// сходит на нет по мере удаления от силуэта. Одной обводкой это не берётся —
// оболочка умеет нарисовать полосу постоянной ширины и не знает, насколько
// далеко её пиксель отстоит от тела, поэтому затухания у неё взяться неоткуда.
// Блум её только слегка размывает: он работает от яркости, а не от расстояния.
//
// Правильный способ — экранный проход: маска враждебных, размытие, подмешать
// снаружи силуэта. Здесь он не выбран сознательно. Рендерер проекта стоит в
// deferred (m_RenderingMode: 2), а собственный полноэкранный проход в deferred
// упирается в stencil, который URP там уже занимает под флаги материалов;
// цена такой правки — риск сломать существующий кадр ради эффекта.
//
// Затухание собирается геометрией: несколько оболочек разной ширины, от самой
// широкой и слабой к узкой и плотной. Рядом с телом накладываются все, дальше
// остаются только широкие — получается ровно та убывающая кривая, ради которой
// затевалось размытие. Смешивание обычное альфа-канальное, а не аддитивное:
// у составного меша задние грани лежат друг за другом по несколько штук на
// пиксель, и при аддитивном смешивании каждая добавляла бы свой вклад — на
// сгибах лап вспыхивали бы горячие пятна. Альфа-канальное сходится к цвету
// свечения и держит потолок.

struct GlowAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    // Сюда импорт кладёт нормаль, сшитую по положению вершины.
    // См. RazlomCharacterImport.OnPostprocessMesh.
    float4 tangentOS : TANGENT;
};

struct GlowVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionOS : TEXCOORD0;
};

// Высота контактной полосы выражена в объектных координатах 0..1. Импортёр
// нормализует меши персонажей к одной высоте, поэтому одна настройка работает
// для Guardian и RootSwarm и не зависит от мирового масштаба экземпляра.
half _GroundGlowBand;
half _GroundGlowFeather;

GlowVaryings RazlomGlowVert(GlowAttributes input, float widthScale, float depthScale)
{
    GlowVaryings output;
    output.positionOS = input.positionOS.xyz;
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

    // Растягиваем по сшитой нормали, как и основная обводка: на жёстких рёбрах
    // и швах развёртки обычные нормали расходятся и оболочка рвётся.
    float3 smoothOS = input.tangentOS.xyz;
    float3 extrudeOS = dot(smoothOS, smoothOS) > 1e-4
        ? normalize(smoothOS)
        : input.normalOS;
    float3 normalWS = TransformObjectToWorldNormal(extrudeOS);

    // Отступ вглубь тот же по смыслу, что у обводки, но немного меньше.
    //
    // Первый заход срезал его до трети, чтобы ореол доставал до ступней, — и
    // это оказалось ошибкой. У составного меша задняя грань лапы лежит ближе
    // камеры, чем передняя грань торса; при широкой оболочке и слабом отступе
    // она проходит ZTest и красит торс поверх. Свечение перестаёт обводить
    // фигуру и заливает её целиком — враг выглядит горящим, а не подсвеченным.
    // Отступ вернулся почти к обводочному: ореол у самой земли слабее, зато
    // тело остаётся телом.
    float3 toCameraWS = GetWorldSpaceNormalizeViewDir(positionWS);
    positionWS -= toCameraWS * (_OutlineDepthBias * depthScale);

    output.positionCS = TransformWorldToHClip(positionWS);

    // Используем ту же выпуклую маску, что и у ink-контура. Линейное
    // расширение подсвечивало нормали, смотрящие в камеру, и на составных
    // мешах давало внутреннюю бахрому между лапами, торсом и рогами.
    // Кубическая маска оставляет расширение только на настоящем силуэте.
    float3 normalVS = TransformWorldToViewDir(normalWS, true);
    float2 direction = normalVS.xy;
    float directionLength = length(direction);
    float2 pixelSize = 2.0 / _ScreenParams.xy;

    // ШИРИНА ПРИВЯЗАНА К 1080p, А НЕ К ПИКСЕЛЮ КАК ТАКОВОМУ.
    //
    // Ореол задан в пикселях экрана, и без этой поправки он остаётся одной и
    // той же ширины при любом разрешении — а фигура нет. В окне редактора на
    // 1312x668 те же двенадцать пикселей ложатся на вдвое меньшее тело, и
    // подсветка превращается в пожар. Ширина должна быть долей ФИГУРЫ, а
    // фигура на экране пропорциональна высоте кадра: камера ортографическая,
    // и её вертикальный размер в метрах от разрешения не зависит.
    float resolutionScale = _ScreenParams.y / 1080.0;

    float silhouette = directionLength * directionLength * directionLength;
    output.positionCS.xy += (direction / max(directionLength, 0.0001)) *
                            silhouette * pixelSize * resolutionScale *
                            _OutlineWidth * widthScale * output.positionCS.w;

    return output;
}

half4 RazlomGlowFrag(GlowVaryings input, half alpha)
{
    // Ширина ноль — обычный персонаж. Оболочку надо снять целиком, а не
    // рисовать нулевой ширины: задние грани всё равно проступают сквозь
    // жёсткие рёбра тонкой линией.
    clip(_OutlineWidth - 0.001h);
    // Оставляем glow только у основания. Верхний край полосы мягко затухает,
    // чтобы контактная линия читалась как подсветка пола, а не как рамка тела.
    half band = max(_GroundGlowBand, 0.02h);
    half feather = max(_GroundGlowFeather, 0.005h);
    half groundMask = 1.0h - smoothstep(band - feather, band, input.positionOS.y);
    // Не обрезаем оболочку целиком: FBX pivot и object-Y у персонажей различаются.\n    // Маска только ослабляет верх, сохраняя читаемый тонкий силуэт.\n
    // Свечение гаснет вместе с телом. Ширину гасит и C#, но осыпающийся труп
    // ещё существует как геометрия, и ореол вокруг него читался бы как живой.
    half fade = saturate(1.0h - _DeathFade);
    return half4(_OutlineColor.rgb, alpha * fade * groundMask);
}

#endif

