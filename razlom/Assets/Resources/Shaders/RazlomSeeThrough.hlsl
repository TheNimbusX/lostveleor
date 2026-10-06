#ifndef RAZLOM_SEE_THROUGH_INCLUDED
#define RAZLOM_SEE_THROUGH_INCLUDED

// ХОЗЯИН ЧАЩИ — «СКВОЗЬ БОССА ВИДНО ГЕРОЯ» (владелец 02.10: «прозрачность должна быть,
// чтоб было видно»). Сетчатая прозрачность: пиксель тела либо рисуется целиком, либо
// выбит по порядку Байера 4×4 — без смешивания, без высветления, тень остаётся.
//
// Выбиваются только пиксели, которые на экране в круге вокруг героя И по земле ближе к
// камере, чем его вертикальная ось (от ступней до головы одинаково): земля и всё, что за
// героем, остаются целыми. Круг — в метрах
// плоскости экрана (камера боя ортографическая; для перспективы круг меряется на
// глубине героя).
//
// Значения пишет ThicketMasterCombatView блоком свойств на рендеры босса и его накладок.
// Это НЕ свойства материала (их нет в Properties и в UnityPerMaterial): без блока они
// берутся глобальными, а глобально их никто не ставит — ноль, прозрачности нет. Поэтому
// шейдер безопасен для любого другого пользователя.
//
// Положение пикселя в мире восстанавливается из SV_POSITION так же, как у декалей URP
// (ShaderPassDecal.hlsl: positionCS.xy / размер цели → ComputeWorldSpacePosition с
// UNITY_MATRIX_I_VP): одинаково для ForwardLit, GBuffer, DepthOnly и DepthNormals, так
// что глубина и цвет выбиваются в одних и тех же пикселях.

float4 _RazlomSeeThroughCenter;  // xyz — середина героя в мире, м; w не читается
float  _RazlomSeeThroughRadius;  // радиус круга в плоскости экрана, м
float  _RazlomSeeThroughDepth;   // на сколько метров по земле ближе оси героя выбивание доходит до полного
float  _RazlomSeeThroughAmount;  // 0…1: доля выбитых пикселей в середине круга (уже с плавным появлением)

// ЛУННАЯ КРОМКА (ревью владельца 02.10 вечер, находка 9: «половина арены в глубокой синей тени, босс
// там пропадает»). Холодный свет только по краю силуэта (френель по нормали вершины), сверху сильнее
// (луна над ареной), и только там, где тело само тёмное: на освещённом теле кромки нет. Тело не
// высветляется — середина силуэта и всё, что смотрит на камеру, не меняются.
// Пишет ThicketMasterPhaseDressing блоком свойств на слоты тела (не свойство материала): без блока —
// ноль, кромки нет, шейдер рисует как URP Lit.
float4 _RazlomBossRim;           // rgb — цвет × сила (линейный); w не читается

// Положение пикселя в мире из SV_POSITION (как у декалей URP): одинаково для всех проходов.
float3 RazlomPixelWorldPosition(float4 positionCS)
{
    float2 positionSS = positionCS.xy * (GetScaledScreenParams().zw - 1.0);
    float deviceDepth = positionCS.z;
#if !UNITY_REVERSED_Z
    deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, deviceDepth);
#endif
    return ComputeWorldSpacePosition(positionSS, deviceDepth, UNITY_MATRIX_I_VP);
}

// Кромка этого пикселя (добавить к цвету прохода ForwardLit). lit — уже освещённый цвет пикселя:
// по нему кромка гаснет на светлом (затвор 0,03 → 0,22 линейной яркости).
half3 RazlomBossRim(float4 positionCS, float3 normalWS, half3 lit)
{
    UNITY_BRANCH
    if (max(_RazlomBossRim.r, max(_RazlomBossRim.g, _RazlomBossRim.b)) <= 0.0)
        return half3(0.0, 0.0, 0.0);
    float3 rimNormal = normalize(normalWS);
    float3 rimView = GetWorldSpaceNormalizeViewDir(RazlomPixelWorldPosition(positionCS));
    float rimEdge = 1.0 - saturate(dot(rimNormal, rimView));
    rimEdge = rimEdge * rimEdge * rimEdge;                 // узко: только край силуэта
    float rimMoon = saturate(rimNormal.y * 0.6 + 0.4);     // верх силуэта ярче низа
    half rimDark = 1.0 - smoothstep(0.03, 0.22, dot(lit, half3(0.2126, 0.7152, 0.0722)));
    return (half3)_RazlomBossRim.rgb * (half)(rimEdge * rimMoon) * rimDark;
}

// Порог Байера 4×4 для пикселя: (k + 0,5) / 16, k = 0…15. Только float-операции.
float RazlomBayer2(float2 a)
{
    a = floor(a);
    return frac(a.x * 0.5 + a.y * a.y * 0.75);
}

float RazlomBayer4(float2 pixel)
{
    return RazlomBayer2(pixel * 0.5) * 0.25 + RazlomBayer2(pixel) + 0.03125;
}

// Доля выбивания в этом пикселе (0 — пиксель целый).
float RazlomSeeThroughCoverage(float4 positionCS)
{
    float3 positionWS = RazlomPixelWorldPosition(positionCS);
    float3 pixelVS = TransformWorldToView(positionWS);
    float3 heroVS = TransformWorldToView(_RazlomSeeThroughCenter.xyz);

    // «Перед героем» — по ЗЕМЛЕ: насколько пиксель ближе к камере, чем вертикальная ось героя,
    // вдоль горизонтального взгляда камеры. Не по глубине вида от середины героя: камера
    // боя смотрит сверху под 48°, ступни на 0,7 м дальше середины, и тело, закрывшее ноги,
    // оставалось бы целым; а крона за героем и над ним — выбивалась бы.
    // Взгляд почти отвесный (горизонталь вырождена) — прежняя мера, глубина вида; вид
    // смотрит вдоль −Z: ближе к камере — больше z.
    float ramp = max(_RazlomSeeThroughDepth, 0.001);
    float3 viewForward = GetViewForwardDir();
    float flatLength = length(viewForward.xz);
    float ahead = flatLength > 0.05
        ? dot(_RazlomSeeThroughCenter.xz - positionWS.xz, viewForward.xz / flatLength)
        : pixelVS.z - heroVS.z;
    float front = saturate(ahead / ramp);

    // Перспектива: точка пикселя сводится на глубину героя, круг одного размера на экране.
    float toHeroPlane = IsPerspectiveProjection() ? heroVS.z / min(pixelVS.z, -0.0001) : 1.0;
    float dist = length(pixelVS.xy * toHeroPlane - heroVS.xy);
    float radius = max(_RazlomSeeThroughRadius, 0.001);
    float circle = 1.0 - smoothstep(radius * 0.55, radius, dist);

    return saturate(_RazlomSeeThroughAmount) * circle * front;
}

// Выбить пиксель по сетке. Amount 0 — порог Байера всегда больше нуля, ничего не выбито.
void RazlomSeeThroughClip(float4 positionCS)
{
    clip(RazlomBayer4(positionCS.xy) - RazlomSeeThroughCoverage(positionCS));
}

#endif // RAZLOM_SEE_THROUGH_INCLUDED
