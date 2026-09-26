// Материал «Дым и свет» (владелец, 25 сентября 2026): дым, мазки кистью и огненный свет интерфейса.
//
// Один шейдер на оба вида, смешивание — предумноженная прозрачность (One, OneMinusSrcAlpha):
//  - дым и мазки (_Light = 0): белая маска спрайта, цвет даёт вершина (тема), тёмная краска
//    закрывает мир;
//  - свет (_Light = 1): альфа выхода 0, цвет прибавляется к миру, как у UI Additive.
//
// Живость:
//  - дым медленно течёт: выборка спрайта сдвигается бесшовным шумом (_NoiseTex RG), плотность
//    дышит (B);
//  - свет пульсирует и по нитям бежит ток;
//  - проявление: чернила растекаются от точки начала (UiInkReveal) с неровным фронтом шума,
//    по фронту горит тлеющая кромка.
//
// Данные элемента пишет UiInkReveal (BaseMeshEffect):
//  - uv1 = (скрыто 0..1, зерно, начало x, начало y);
//  - uv2 = (место внутри элемента 0..1 по x и y, сила тлеющей кромки, ширина кромки).
//    Ширина — множитель к _EdgeWidth (UiInkReveal.EdgeScale, 0 читается как 1): шире — кромка
//    тусклее и гаснет дольше (итоги забега тлеют медленно, пауза и подсказки — как были).
// Холсту нужны каналы TexCoord1 и TexCoord2. Без них uv1 = 0: «скрыто 0», элемент просто виден.
//
// Часы — _UiTime (реальное время интерфейса, ставит UiInkClock): в паузе timeScale = 0,
// а дым в меню паузы должен течь.
Shader "Razlom/UI Ink"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [NoScaleOffset] _NoiseTex ("Шум (RG — течение, B — плотность)", 2D) = "gray" {}
        [Toggle] _Light ("Свет (прибавляется к миру)", Float) = 0
        _FlowScale ("Масштаб течения", Float) = 1.3
        _FlowSpeed ("Скорость течения", Float) = 0.035
        _FlowAmount ("Сила течения (доля спрайта)", Float) = 0.018
        _Breath ("Дыхание плотности", Range(0, 1)) = 0.35
        _Pulse ("Пульс света", Range(0, 1)) = 0
        _PulseSpeed ("Частота пульса", Float) = 2.2
        _Shimmer ("Ток по свету", Range(0, 2)) = 0
        _ShimmerAxis ("Ось тока (x, y)", Vector) = (1, 0, 0, 0)
        _EdgeColor ("Кромка проявления", Color) = (1, 0.46, 0.16, 1)
        _EdgeWidth ("Ширина фронта", Range(0.01, 0.4)) = 0.06
        _EdgeGlow ("Сила кромки", Range(0, 6)) = 1.6
        _Wisp ("Светлые края дыма", Color) = (0.2, 0.25, 0.31, 1)
        _WispAmount ("Сила светлых краёв", Range(0, 1.5)) = 1
        [NoScaleOffset] _ShapeTex ("Мягкая форма (для карты)", 2D) = "white" {}
        [Toggle(INK_SHAPE)] _UseShape ("Обрезать по форме", Float) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma shader_feature_local _ INK_SHAPE

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 params   : TEXCOORD1;
                float4 local    : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 params   : TEXCOORD2;
                float4 local    : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _NoiseTex;
            sampler2D _ShapeTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _Light, _FlowScale, _FlowSpeed, _FlowAmount, _Breath, _Pulse, _PulseSpeed, _Shimmer;
            float4 _ShimmerAxis;
            fixed4 _EdgeColor;
            float _EdgeWidth, _EdgeGlow;
            fixed4 _Wisp;
            float _WispAmount;
            float _UiTime;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                OUT.params = v.params;
                OUT.local = v.local;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float t = _UiTime;
                float hidden = saturate(IN.params.x);
                float seed = IN.params.y;
                float2 local = IN.local.xy;
                float burn = IN.local.z;

                // Течение: выборка спрайта плывёт по бесшовному шуму. Смещение в долях спрайта,
                // поэтому крупный и мелкий дым текут одинаково заметно.
                float2 drift = float2(t * _FlowSpeed, t * _FlowSpeed * 0.63);
                float2 flow = tex2D(_NoiseTex, local * _FlowScale + drift + seed).rg - 0.5;
                half4 tex = tex2D(_MainTex, IN.texcoord + flow * _FlowAmount) + _TextureSampleAdd;

                half strength = tex.a * IN.color.a;
                half density = tex2D(_NoiseTex, local * _FlowScale * 0.55 - drift * 0.7 + seed * 1.7).b;
                strength *= lerp(1.0, saturate(0.45 + density * 1.1), _Breath);

                #ifdef INK_SHAPE
                strength *= tex2D(_ShapeTex, local).a;
                #endif

                // Свет: пульс со сдвигом фазы по зерну и ток — яркость бежит вдоль элемента.
                half light = 1.0 + _Pulse * sin(t * _PulseSpeed + seed * 6.2831);
                // Ток бежит вдоль оси: у нити — вдоль x, у трещины меню — вдоль y.
                float along = dot(local, _ShimmerAxis.xy);
                float across = dot(local, _ShimmerAxis.yx);
                half current = tex2D(_NoiseTex, float2(along * 1.7 - t * 0.21, across * 0.4 + seed)).b;
                light *= 1.0 + _Shimmer * (current - 0.45);

                // Проявление: фронт идёт от точки начала, неровный от шума. hidden = 1 — не видно,
                // 0 — видно целиком. Кромка горит, пока элемент проявляется.
                half3 emission = 0;
                if (hidden > 0.001)
                {
                    // Ширина фронта элемента: 0 в uv2.w — старая сетка, ширина как у материала.
                    float scale = IN.local.w > 0.001 ? IN.local.w : 1.0;
                    float width = _EdgeWidth * scale;
                    float2 origin = IN.params.zw;
                    float reach = distance(local, origin) / 1.2;
                    float grain = tex2D(_NoiseTex, local * 2.3 + seed * 3.1).b;
                    float front = saturate(reach * 0.7 + grain * 0.3);
                    float progress = (1.0 - hidden) * (1.0 + width * 2.0);
                    float edge = progress - front;
                    float visible = smoothstep(0.0, width, edge);
                    // Кромка — тонкая тлеющая линия по фронту, а не полоса: квадрат спада и узкое окно.
                    float rim = saturate(1.0 - abs(edge - width * 0.5) / (width * 0.7));
                    // Широкая кромка тлеет, а не горит: свет делится на корень ширины, и гаснет она
                    // не на последней четверти проявления, а плавно на более длинном отрезке.
                    float wide = max(scale, 1.0);
                    float fade = saturate(hidden * 4.0 / wide);
                    emission = _EdgeColor.rgb * rim * rim * (_EdgeGlow * rsqrt(wide)) * burn * tex.a * IN.color.a * fade;
                    strength *= visible;
                }

                #ifdef UNITY_UI_CLIP_RECT
                half clip2D = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                strength *= clip2D;
                emission *= clip2D;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(strength + dot(emission, 1) - 0.001);
                #endif

                half3 colour = tex.rgb * IN.color.rgb * strength * (_Light > 0.5 ? light : 1.0);

                // Дым — не чёрная клякса: редкие края клубов и завихрения внутри чуть светлее (как в
                // концепте), иначе на тёмной земле низа экрана дым не читается формой.
                if (_Light < 0.5)
                {
                    half wisp = smoothstep(0.04, 0.35, tex.a) * (1.0 - smoothstep(0.35, 0.95, tex.a));
                    half swirl = tex2D(_NoiseTex, local * _FlowScale * 1.9 + drift * 1.4 + seed * 0.7).g;
                    colour += _Wisp.rgb * _WispAmount * (wisp * 0.9 + smoothstep(0.45, 0.85, swirl) * 0.45 * tex.a) * strength;
                }
                half alpha = _Light > 0.5 ? 0.0 : strength;
                return half4(colour + emission, alpha);
            }
        ENDCG
        }
    }
}
