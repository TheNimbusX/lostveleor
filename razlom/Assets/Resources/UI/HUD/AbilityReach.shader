// Досягаемость способности на земле в материале «Дым и свет» (владелец 26 сентября: огненная нить
// «ни с чем не сочетается»). Фигура — сетка с расстоянием до края в вершинах (HudRangePreview):
// uv0.x — метры до кромки (плюс внутри, минус в кайме), uv0.y — на какой глубине гаснет заливка
// (кольцо), uv0.z — сила фигуры. Внутри — тёмный дым, как акварельная заливка: пигмент копится у края
// и медленно дышит. По краю — тонкая тёплая кремовая кромка постоянной экранной толщины с мягким
// ореолом и лёгким мерцанием, снаружи — едва заметная тень, чтобы контур читался на светлой траве.
// Смешивание предумноженное: дым затемняет землю, свет прибавляется.
//
// Что прячет фигуру. Тела и стволы — да, трава и камни по колено — нет. Делает это обычный тест
// глубины по буферу камеры, но вершина для него сдвинута к камере вдоль луча взгляда на _Lift метров:
// на экране она стоит там же, где лежит на земле, а глубина у неё — как у точки на высоте колена.
// Первая версия «Дыма и света» рисовала поверх всего (ZTest Always) и гасила фигуру по
// _CameraDepthTexture — и в собранном плеере фигуры не было вовсе. Текстура глубины в PC_Renderer
// собирается DepthOnly-проходом до непрозрачных (его требует SSAO «до непрозрачных»), а у
// Razlom/Texture Toon такого прохода нет: тел в ней нет, прятать за телами она не могла. Буфер глубины
// камеры есть всегда и содержит всё непрозрачное; этим же путём рисовались ленты досягаемости до
// 26 сентября — и были видны.
Shader "Hidden/Razlom/AbilityReach"
{
    Properties
    {
        _Rim ("Кромка", Color) = (1, .87, .72, .9)
        _Smoke ("Дым", Color) = (.03, .045, .07, 1)
        _Fill ("Плотность дыма", Range(0, 1)) = .16
        _Shimmer ("Мерцание", Range(0, 1)) = 1
        _Lift ("Не прятать за тем, что ближе к камере, чем, м", Float) = 1
        _Appear ("Появление", Range(0, 1)) = 1
        _Fringe ("Ширина каймы, м", Float) = .18
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off Cull Off ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float4 edge:TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float4 edge:TEXCOORD0;
                float3 world:TEXCOORD1;
            };
            half4 _Rim, _Smoke;
            float _Fill, _Shimmer, _Lift, _Appear, _Fringe;

            float ReachHash(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }
            float ReachNoise(float2 p)
            {
                float2 c = floor(p);
                float2 f = frac(p);
                f = f * f * (3. - 2. * f);
                return lerp(lerp(ReachHash(c), ReachHash(c + float2(1, 0)), f.x),
                            lerp(ReachHash(c + float2(0, 1)), ReachHash(c + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.world = TransformObjectToWorld(v.positionOS.xyz);
                // Сдвиг вдоль луча к камере не меняет место на экране ни в орто, ни в перспективе —
                // только глубину. При наклоне камеры 48° метр по лучу — около 0,75 м высоты: трава и
                // камни ниже этого фигуру не закрывают, тело героя и стволы — закрывают.
                float3 toCamera = normalize(GetWorldSpaceViewDir(o.world));
                o.positionCS = TransformWorldToHClip(o.world + toCamera * _Lift);
                o.edge = v.edge;
                return o;
            }

            half4 frag(Varyings i):SV_Target
            {
                float d = i.edge.x;
                // Метров в пикселе поперёк края: толщина кромки и ореола задаётся в пикселях экрана.
                float px = max(fwidth(d), 1e-5);
                float p = d / px;
                float strength = i.edge.z;
                float t = _Time.y;
                float2 w = i.world.xz;

                // Заливка: дым гуще у края (акварельный затёк) и медленно дышит.
                float n = ReachNoise(w * .85 + float2(t * .07, -t * .05)) * .65
                        + ReachNoise(w * 2.3 - float2(t * .11, t * .04)) * .35;
                float inside = smoothstep(-.5, .8, p);
                float pool = .6 + .4 * exp(-max(d, 0.) / .45);
                float fade = 1. - smoothstep(i.edge.y * .3, i.edge.y, d);
                float fill = _Fill * strength * inside * pool * fade * (.7 + .6 * n);

                // Край каймы гаснет плавно: ни ореол, ни тень не обрываются ступенькой.
                float tail = saturate((d + _Fringe) / (_Fringe * .4));
                float shade = exp(-max(-p, 0.) * .25) * (1. - inside) * .14 * strength * tail;

                // Кромка: линия ≈1,6 px чуть внутри края и ореол — внутрь длиннее, наружу короче.
                float along = w.x * 1.3 + w.y * 1.7;
                float shimmer = lerp(1., .84 + .16 * sin(along * 2.1 - t * 1.7) * sin(along * .8 + t * .9)
                                         + .05 * sin(t * 1.9), _Shimmer);
                float core = 1. - smoothstep(.55, 1.35, abs(p - 1.1));
                float halo = exp(-max(p - 1., 0.) * .22 - max(1. - p, 0.) * .45) * .32 * tail;
                float rim = (core + halo) * strength * shimmer * _Rim.a;

                float smoke = fill + shade;
                half3 color = _Smoke.rgb * smoke + _Rim.rgb * rim;
                float alpha = saturate(smoke + core * .55 * strength * _Rim.a);
                return half4(color * _Appear, alpha * _Appear);
            }
            ENDHLSL
        }
    }
}
