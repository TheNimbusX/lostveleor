// Комикс-рисовка мира (проба 01.10): нормали из глубины, кисть (обобщённый Кувахара), тон и тушь.
// Проходы ставит ComicStyleFeature после непрозрачных и неба, до прозрачных. Кривые тона повторяют
// ComicStyleRules.ToneTerrace / ToneLuminance строка в строку — при правке менять обе стороны.
Shader "Hidden/Razlom/Comic Style"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X_FLOAT(_ComicDepth);
        TEXTURE2D_X(_ComicNormals);
        TEXTURE2D_X(_ComicPainted);
        TEXTURE2D_X(_ComicKey);
        TEXTURE2D_X(_ComicKeyGrid);

        float4 _ComicTexel;        // 1/w, 1/h, w, h — кадр полного разрешения
        float4 _ComicSourceTexel;  // шаг кисти по исходнику (в половинном режиме — два пикселя)
        float4 _ComicCamera;       // near, far, ортографическая, reversed Z
        float4 _ComicZBuffer;      // как _ZBufferParams, посчитаны в C#
        float4 _ComicProjection;   // 1/P00, 1/P11
        float4 _ComicPaint;        // радиус ядра, доля кисти, резкость q, жёсткость
        float4 _ComicInkColor;     // линейный цвет туши, непрозрачность
        float4 _ComicInk;          // толщина px, колец, порог разрыва глубины (м), мягкость (м)
        float4 _ComicInkSmall;     // порог малого разрыва глубины (м), мягкость (м) — только со сменой цвета
        float4 _ComicEdge;         // cos порога излома, ширина по cos, цвет: нижний и верхний порог
        float4 _ComicTone;         // сила, ширина полосы в стопах, середина полосы, жёсткость
        float4 _ComicShadowTint;   // тёплый оттенок тени (яркость 1), доля
        float4 _ComicGrade;        // насыщенность, добавка в тенях, яркость
        float4 _ComicToneKey;      // середина по кадру (1/0), множитель, пределы середины
        // Веса восьми секторов кисти на каждую выборку ядра, уже с гауссианой (ComicStyleRules.PainterlyTapWeights):
        // [2i] — секторы 0–3, [2i+1] — 4–7. 169 выборок — ядро радиуса 6.
        float4 _ComicPaintWeights[338];

        float RawDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_ComicDepth, sampler_PointClamp, uv, 0).r;
        }

        float EyeDepth(float raw)
        {
            // Камера боя ортографическая: глубина линейна между near и far.
            if (_ComicCamera.z > 0.5)
                return lerp(_ComicCamera.x, _ComicCamera.y, _ComicCamera.w > 0.5 ? 1.0 - raw : raw);
            return 1.0 / (_ComicZBuffer.z * raw + _ComicZBuffer.w);
        }

        float EyeAt(float2 uv) { return EyeDepth(RawDepth(uv)); }

        bool IsFar(float raw)
        {
            return _ComicCamera.w > 0.5 ? raw <= 1e-7 : raw >= 1.0 - 1e-7;
        }

        float3 ViewPosition(float2 uv)
        {
            float eye = EyeAt(uv);
            float2 ndc = uv * 2.0 - 1.0;
            float2 xy = ndc * _ComicProjection.xy * (_ComicCamera.z > 0.5 ? 1.0 : eye);
            return float3(xy, eye);
        }

        float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

        // Сжатие HDR в 0..1 и обратно: кисть сравнивает дисперсии в ограниченном диапазоне.
        float3 Compress(float3 c) { return c / (1.0 + max(c.r, max(c.g, c.b))); }
        float3 Expand(float3 c) { return c / max(1e-4, 1.0 - max(c.r, max(c.g, c.b))); }
        ENDHLSL

        // 0 — нормали из глубины. Разность берётся с той стороны, где глубина ровнее: на кромке
        // предмета нормаль не «перетекает» на фон. Зеркало по y не меняет углов между нормалями.
        Pass
        {
            Name "Comic normals"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 t = _ComicTexel.xy;
                if (IsFar(RawDepth(uv))) return half4(0.5, 0.5, 0.0, 0.0);
                float3 p = ViewPosition(uv);
                float3 pl = ViewPosition(uv - float2(t.x, 0.0));
                float3 pr = ViewPosition(uv + float2(t.x, 0.0));
                float3 pd = ViewPosition(uv - float2(0.0, t.y));
                float3 pu = ViewPosition(uv + float2(0.0, t.y));
                float3 dx = abs(pr.z - p.z) < abs(p.z - pl.z) ? pr - p : p - pl;
                float3 dy = abs(pu.z - p.z) < abs(p.z - pd.z) ? pu - p : p - pd;
                float3 n = cross(dy, dx);
                float lengthSq = dot(n, n);
                n = lengthSq > 1e-20 ? n * rsqrt(lengthSq) : float3(0.0, 0.0, -1.0);
                n = n.z > 0.0 ? -n : n;
                return half4(n * 0.5 + 0.5, 1.0);
            }
            ENDHLSL
        }

        // 1 — кисть: обобщённый фильтр Кувахары с полиномиальными весами (8 секторов). Мазок живёт
        // в самой картинке, а не в экранной текстуре, поэтому не «плывёт» вместе с камерой.
        Pass
        {
            Name "Comic paint"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float3 original = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                int radius = (int)_ComicPaint.x;
                if (radius < 1) return half4(original, 1.0);

                float4 m[8];
                float3 s[8];
                [unroll] for (int k = 0; k < 8; k++) { m[k] = 0.0; s[k] = 0.0; }

                // Веса секторов зависят только от смещения выборки — их считает C# один раз
                // (ComicStyleRules.PainterlyTapWeights), здесь только выборка и умножения.
                float2 stepUv = _ComicSourceTexel.xy;
                int index = 0;
                [loop] for (int y = -radius; y <= radius; y++)
                {
                    [loop] for (int x = -radius; x <= radius; x++)
                    {
                        float3 c = Compress(max(0.0, SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp,
                            uv + float2(x, y) * stepUv, 0).rgb));
                        float3 cc = c * c;
                        float4 wa = _ComicPaintWeights[index];
                        float4 wb = _ComicPaintWeights[index + 1];
                        index += 2;
                        m[0] += float4(c * wa.x, wa.x); s[0] += cc * wa.x;
                        m[1] += float4(c * wa.y, wa.y); s[1] += cc * wa.y;
                        m[2] += float4(c * wa.z, wa.z); s[2] += cc * wa.z;
                        m[3] += float4(c * wa.w, wa.w); s[3] += cc * wa.w;
                        m[4] += float4(c * wb.x, wb.x); s[4] += cc * wb.x;
                        m[5] += float4(c * wb.y, wb.y); s[5] += cc * wb.y;
                        m[6] += float4(c * wb.z, wb.z); s[6] += cc * wb.z;
                        m[7] += float4(c * wb.w, wb.w); s[7] += cc * wb.w;
                    }
                }

                float4 result = 0.0;
                [unroll] for (int n = 0; n < 8; n++)
                {
                    float weight = max(m[n].w, 1e-6);
                    float3 mean = m[n].rgb / weight;
                    float3 variance = abs(s[n] / weight - mean * mean);
                    float sigma2 = variance.r + variance.g + variance.b;
                    float alpha = 1.0 / (1.0 + pow(max(1e-8, _ComicPaint.w * 1000.0 * sigma2), 0.5 * _ComicPaint.z));
                    result += float4(mean * alpha, alpha);
                }
                float3 painted = Expand(saturate(result.rgb / max(result.w, 1e-6)));
                return half4(lerp(original, painted, saturate(_ComicPaint.y)), 1.0);
            }
            ENDHLSL
        }

        // 2 — кисть половинного разрешения поверх исходного кадра полного разрешения.
        Pass
        {
            Name "Comic paint mix"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float3 original = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;
                float3 painted = SAMPLE_TEXTURE2D_X_LOD(_ComicPainted, sampler_LinearClamp, uv, 0).rgb;
                return half4(lerp(original, painted, saturate(_ComicPaint.y)), 1.0);
            }
            ENDHLSL
        }

        // 3 — тон (2–3 ступени, тёплые тени, насыщенность) и тёплая тушь поверх.
        Pass
        {
            Name "Comic ink and tone"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // = ComicStyleRules.ToneTerrace
            float ToneTerrace(float f, float hardness)
            {
                float w = max(1e-3, 1.0 - saturate(hardness));
                float t = saturate(f) - 0.5;
                float s = smoothstep(0.5 - 0.5 * w, 0.5, abs(t));
                return 0.5 + sign(t) * 0.5 * s;
            }

            // = ComicStyleRules.TonePivotFromKey; без кадра — ручная середина.
            float TonePivot()
            {
                if (_ComicToneKey.x < 0.5) return _ComicTone.z;
                float2 key = SAMPLE_TEXTURE2D_X_LOD(_ComicKey, sampler_PointClamp, float2(0.5, 0.5), 0).rg;
                if (key.y < 0.5) return _ComicTone.z;
                return clamp(exp2(key.x) * _ComicToneKey.y, _ComicToneKey.z, _ComicToneKey.w);
            }

            // = ComicStyleRules.ToneLuminance
            float ToneLuminance(float l, float pivot)
            {
                float strength = saturate(_ComicTone.x);
                if (l <= 0.0 || strength <= 0.0) return max(l, 0.0);
                float stops = _ComicTone.y;
                float p = log2(max(l, 1e-4) / pivot) / stops + 0.5;
                float i = floor(p);
                float q = i + ToneTerrace(p - i, _ComicTone.w);
                float stepped = p + (q - p) * strength;
                return pivot * exp2((stepped - 0.5) * stops);
            }

            // = ComicStyleRules.BrightenPeak: до колена — множитель, выше — плечо к порогу Bloom (1,0).
            float BrightenPeak(float peak, float gain, float knee)
            {
                float lifted = peak * gain;
                if (gain <= 1.0 || lifted <= knee) return lifted;
                float head = max(1e-3, 1.0 - knee);
                float rolled = knee + head * (1.0 - exp(-(lifted - knee) / head));
                return max(rolled, peak);
            }

            // Подъём яркости по самому яркому каналу: оттенок и насыщенность те же, белое не выгорает.
            float3 Brighten(float3 c)
            {
                float peak = max(c.r, max(c.g, c.b));
                if (peak <= 1e-5) return c * _ComicGrade.z;
                return c * (BrightenPeak(peak, _ComicGrade.z, _ComicGrade.w) / peak);
            }

            float3 Grade(float3 c, float pivot)
            {
                c = max(c, 0.0);
                float l = Luma(c);
                float toned = ToneLuminance(l, pivot);
                c *= toned / max(l, 1e-4);
                // = ComicStyleRules.ShadowAmount: 0 во всей основной полосе, 1 — от середины полосы ниже.
                float below = -log2(max(toned, 1e-6) / pivot) / _ComicTone.y;
                float shadow = saturate((below - 0.5) * 2.0);
                c *= lerp(float3(1.0, 1.0, 1.0), _ComicShadowTint.rgb, saturate(_ComicShadowTint.a * shadow));
                float saturation = _ComicGrade.x + _ComicGrade.y * shadow;
                float graded = Luma(c);
                c = max(0.0, graded + (c - graded) * saturation);
                return Brighten(c);
            }

            float ColourDifference(float3 a, float3 b)
            {
                float3 pa = sqrt(saturate(a / (1.0 + a)));
                float3 pb = sqrt(saturate(b / (1.0 + b)));
                return length(pa - pb);
            }

            float3 NormalAt(float2 uv, out float valid)
            {
                float4 n = SAMPLE_TEXTURE2D_X_LOD(_ComicNormals, sampler_PointClamp, uv, 0);
                valid = n.a;
                return n.xyz * 2.0 - 1.0;
            }

            float InkAt(float2 uv, float3 centreColour)
            {
                float2 t = _ComicTexel.xy;
                float centreDepth = EyeAt(uv);
                float centreValid;
                float3 centreNormal = NormalAt(uv, centreValid);
                float thickness = _ComicInk.x;
                int rings = (int)_ComicInk.y;
                float ink = 0.0;
                static const float2 axes[4] = { float2(1.0, 0.0), float2(0.0, 1.0), float2(0.70710678, 0.70710678), float2(0.70710678, -0.70710678) };

                [loop] for (int k = 1; k <= rings; k++)
                {
                    // Разрыв глубины: линия только на ближнем предмете — на всю толщину.
                    float depthWeight = saturate(thickness - (k - 1));
                    // Излом метится с обеих сторон — половина толщины на сторону.
                    float normalWeight = saturate(thickness * 0.5 - (k - 1));
                    [unroll] for (int a = 0; a < 4; a++)
                    {
                        float2 offset = axes[a] * (k * t);
                        // Вторая производная глубины: наклонная земля даёт ноль, ступень к фону — полный разрыв.
                        float laplace = EyeAt(uv + offset) + EyeAt(uv - offset) - 2.0 * centreDepth;
                        // Крупный разрыв (герой, корень, камень на фоне) — тушь всегда.
                        float big = saturate((laplace - _ComicInk.z) / max(_ComicInk.w, 1e-4));
                        // Малый разрыв (гриб, куст, мелкий камень) — только со сменой цвета, ниже.
                        float small = saturate((laplace - _ComicInkSmall.x) / max(_ComicInkSmall.y, 1e-4));
                        small = small > big ? small : 0.0;

                        float edge = 0.0;
                        if (normalWeight > 0.0 && centreValid > 0.5)
                        {
                            float valid1, valid2;
                            float3 n1 = NormalAt(uv + offset, valid1);
                            float3 n2 = NormalAt(uv - offset, valid2);
                            float bend = min(valid1 > 0.5 ? dot(centreNormal, n1) : 1.0,
                                             valid2 > 0.5 ? dot(centreNormal, n2) : 1.0);
                            edge = saturate((_ComicEdge.x - bend) / _ComicEdge.y);
                        }
                        if (edge > 0.0 || small > 0.0)
                        {
                            // Трава на траве — без туши; гриб, камень, корень или герой на траве — с тушью.
                            float3 c1 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv + offset, 0).rgb;
                            float3 c2 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv - offset, 0).rgb;
                            float difference = max(ColourDifference(centreColour, c1), ColourDifference(centreColour, c2));
                            float gate = smoothstep(_ComicEdge.z, _ComicEdge.w, difference);
                            edge *= gate;
                            small *= gate;
                        }
                        ink = max(ink, max(big, small) * depthWeight);
                        ink = max(ink, edge * normalWeight);
                    }
                }
                return ink;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float3 colour = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;
                float3 styled = Grade(colour, TonePivot());
                float ink = InkAt(uv, colour);
                styled = lerp(styled, _ComicInkColor.rgb, saturate(ink * _ComicInkColor.a));
                return half4(styled, 1.0);
            }
            ENDHLSL
        }

        // 4 — средняя яркость мира: сводит сетку 40×24 прохода 5 в один пиксель. x — средний log2, y — число выборок.
        // Раньше один поток сам брал 960 выборок из кадра полного разрешения (0,59 мс на RTX 3060);
        // теперь их параллельно берёт сетка, а здесь — 960 чтений крошечной текстуры из кэша.
        Pass
        {
            Name "Comic key"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float sum = 0.0, count = 0.0;
                [loop] for (int y = 0; y < 24; y++)
                {
                    [loop] for (int x = 0; x < 40; x++)
                    {
                        float2 cell = LOAD_TEXTURE2D_X(_ComicKeyGrid, int2(x, y)).rg;
                        sum += cell.x * cell.y;
                        count += cell.y;
                    }
                }
                return half4(count > 0.0 ? sum / count : 0.0, count, 0.0, 1.0);
            }
            ENDHLSL
        }

        // 5 — сетка 40×24 для средней яркости: одна выборка на клетку, фон за краем поляны не считается.
        // x — log2 яркости, y — 1, если выборка есть.
        Pass
        {
            Name "Comic key grid"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                if (IsFar(RawDepth(uv))) return half4(0.0, 0.0, 0.0, 1.0);
                float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                return half4(log2(max(Luma(max(c, 0.0)), 1e-4)), 1.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }
}
