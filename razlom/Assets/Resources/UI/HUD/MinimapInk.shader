// Чернильная карта Разлома (владелец 26 сентября: кромка карты «очень пиксельная, выглядит нелепо»).
//
// HudMinimap раз на уровень печёт поле расстояний до кромки прохода (_MainTex: метры, внутри > 0,
// сглажено), здесь по нему рисуются заливка и мягкая кремовая линия кистью. Сглаживание — в пиксель
// цели, ширина линии — в единицах холста, поэтому край ровный при любом размере уровня и разрешении
// поля, а не лесенка из полуметровых клеток, растянутая в семь раз.
//
// Кисть: край прохода чуть гуляет (крупный шум), ширина линии дышит вдоль неё, ворс (мелкий шум)
// делает мазок неровным по плотности, вокруг — слабое кремовое растекание. Шум привязан к миру:
// карта едет за героем, а мазок на ней стоит.
//
// Параметры ставит HudMinimap.UpdateView:
//  _View   — видимый квадрат мира: x, z, ширина, высота (метры);
//  _Field  — квадрат мира, который покрывает поле;
//  _Decode — метры = значение * x + y (RHalf: 1, 0; R8: из 0..1 в ±предел);
//  _Brush  — x: метров в пикселе цели, y: ширина линии, z: дрожание края, w: метров в единице холста.
// Цвета — в HudMinimap (InkOutside, InkFloor, InkEdge), здесь только значения по умолчанию.
Shader "Hidden/Razlom/MinimapInk"
{
    Properties
    {
        _MainTex ("Поле расстояний", 2D) = "black" {}
        _Outside ("Пустота", Color) = (0.047, 0.063, 0.09, 1)
        _Floor ("Проход", Color) = (0.118, 0.149, 0.192, 1)
        _Edge ("Кромка (альфа — сила)", Color) = (0.95, 0.87, 0.72, 0.9)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _View, _Field, _Decode, _Brush;
            fixed4 _Outside, _Floor, _Edge;

            // Хэш без синусов (Dave Hoskins): одинаковый на всех видеокартах.
            float Hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 world = _View.xy + input.uv * _View.zw;
                float d = tex2D(_MainTex, (world - _Field.xy) / _Field.zw).r * _Decode.x + _Decode.y;
                // Шум в единицах холста, привязанных к миру: мазок одинаковый на любом уровне.
                float2 p = world / max(_Brush.w, 1e-4);
                float aa = max(_Brush.x, 1e-4);

                // Край прохода гуляет вместе с линией: волна в два десятка единиц и помельче.
                float wave = Noise(p * 0.05) * 0.65 + Noise(p * 0.13 + 7.3) * 0.35;
                d += (wave - 0.5) * _Brush.z;
                float floorMask = smoothstep(-aa, aa, d);

                // Линия лежит сразу внутри прохода: её внешний край совпадает с кромкой заливки.
                float width = _Brush.y * (0.75 + 0.5 * Noise(p * 0.07 + 11.7));
                float dist = abs(d - width * 0.5);
                float core = 1.0 - smoothstep(max(width * 0.5 - aa, 0.0), width * 0.5 + aa, dist);
                // Ворс: плотность мазка неровная; вокруг — слабое растекание краски.
                float bristle = lerp(0.7, 1.0, Noise(p * 0.5 + 3.1));
                float bleed = saturate(1.0 - dist / (width * 2.4 + aa));
                float stroke = saturate(core * bristle + bleed * bleed * 0.22);

                // Заливка акварельная: неровная, в глубине прохода чуть светлее, у кромки темнее.
                // Множителями, а не сдвигом: в линейном пространстве тёмные чернила близки к нулю.
                float grain = Noise(p * 0.035 + 19.1) * 0.6 + Noise(p * 0.11 + 5.7) * 0.4;
                float depth = smoothstep(0.0, 2.5, d);
                float3 floorColour = _Floor.rgb * (0.88 + 0.22 * depth) * (0.85 + 0.3 * grain);
                float3 colour = lerp(_Outside.rgb, floorColour, floorMask);
                colour = lerp(colour, _Edge.rgb, stroke * _Edge.a);
                return fixed4(colour, 1.0);
            }
            ENDCG
        }
    }
}
