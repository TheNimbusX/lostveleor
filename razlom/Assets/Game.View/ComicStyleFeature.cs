using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// «Комикс-рисовка» мира поверх тех же моделей (проба 01.10, кадр ART/UI/concepts-2026-10-01-style-shift/
    /// 6-mix-with-our-hud.jpg). Модели, меши, текстуры и материалы не меняются — только картинка камеры:
    /// <list type="number">
    /// <item>после непрозрачных и неба, ДО прозрачных: живописное сглаживание (обобщённый Кувахара малым
    /// радиусом — «кисть» живёт в самой картинке и не плывёт вместе с камерой), 2–3 тона с тёплыми тенями и
    /// тёплая коричневая тушь по разрывам глубины и изломам поверхности. Эффекты паков, метки на земле,
    /// полоски и цифры урона рисуются позже и остаются чёткими;</item>
    /// <item>в самом конце камеры, до экранного HUD: лёгкое размытие диорамы у верхней и нижней кромки.
    /// Весь HUD — Screen Space Overlay: URP рисует его после этой фичи, он не меняется.</item>
    /// </list>
    /// Выключено по умолчанию (<see cref="ComicStyle.Enabled"/>): тогда фича не ставит ни одного прохода и
    /// ничего не запрашивает. Только основная камера игры (тег MainCamera): миникарта, портреты и превью —
    /// как были. Нормали восстанавливаются из глубины: DepthNormals-префас у Texture Toon, листвы и земли
    /// нет, и запрос нормалей выкинул бы героев и лес из текстуры глубины.
    /// </summary>
    [DisallowMultipleRendererFeature("Comic Style (Разлом)")]
    public sealed class ComicStyleFeature : ScriptableRendererFeature
    {
        public const string StyleShaderName = "Hidden/Razlom/Comic Style";
        public const string TiltShaderName = "Hidden/Razlom/Comic Tilt Shift";

        /// <summary>Когда рисуется кисть, тон и тушь: после непрозрачных и неба, до прозрачных.</summary>
        public const RenderPassEvent StylizeEvent = RenderPassEvent.AfterRenderingSkybox;

        /// <summary>
        /// Сразу после рисовки и всё ещё до прозрачных: сюда при включённой рисовке переезжает контур юнитов
        /// (UnitOutlineFeature), иначе кисть сотрёт его тонкую линию.
        /// </summary>
        public const RenderPassEvent AfterStylize = StylizeEvent + 1;

        [SerializeField] private Shader _styleShader;
        [SerializeField] private Shader _tiltShader;
        [SerializeField] private ComicStyleSettings _settings = new ComicStyleSettings();

        private Material _styleMaterial, _tiltMaterial;
        private StylizePass _stylize;
        private TiltShiftPass _tilt;

        // Для UnitOutlineFeature: она стоит в списке раньше и решает про свой проход до этой фичи.
        private static bool s_stylizeReady, s_sceneView;

        public ComicStyleSettings Settings => _settings;
        public Shader StyleShader => _styleShader;
        public Shader TiltShader => _tiltShader;

        public void SetShaders(Shader style, Shader tilt)
        {
            _styleShader = style;
            _tiltShader = tilt;
        }

        public override void Create()
        {
            ReleaseMaterials();
            if (_settings == null) _settings = new ComicStyleSettings();
            _settings.Sanitize();
            if (_styleShader == null) _styleShader = Shader.Find(StyleShaderName);
            if (_tiltShader == null) _tiltShader = Shader.Find(TiltShaderName);
            if (_styleShader != null && _styleShader.isSupported)
            {
                _styleMaterial = CoreUtils.CreateEngineMaterial(_styleShader);
                _stylize = new StylizePass(_styleMaterial, _settings);
            }
            if (_tiltShader != null && _tiltShader.isSupported)
            {
                _tiltMaterial = CoreUtils.CreateEngineMaterial(_tiltShader);
                _tilt = new TiltShiftPass(_tiltMaterial, _settings);
            }
            s_stylizeReady = _stylize != null;
            s_sceneView = _settings.ApplyInSceneView;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Выключено — ни прохода, ни запроса глубины: игра рисуется ровно как без фичи.
            if (!ComicStyle.Enabled) return;
            s_sceneView = _settings.ApplyInSceneView;
            var cameraData = renderingData.cameraData;
            if (!AppliesTo(cameraData.camera, cameraData.cameraType, cameraData.renderType, _settings.ApplyInSceneView)) return;
            _settings.Sanitize();
            if (_stylize != null) renderer.EnqueuePass(_stylize);
            if (_tilt != null && _settings.TiltShift && _settings.TiltStrength > 0f && _settings.TiltBlur > 0f)
                renderer.EnqueuePass(_tilt);
        }

        /// <summary>
        /// Основная камера игры: базовая, тег MainCamera. Съёмка (-razlom-capture) рендерит ту же камеру в
        /// RenderTexture — поэтому отбор по тегу, а не по «рисует в экран».
        /// </summary>
        public static bool AppliesTo(Camera camera, CameraType type, CameraRenderType renderType, bool sceneView)
        {
            if (camera == null || renderType != CameraRenderType.Base) return false;
            if (type == CameraType.SceneView) return sceneView;
            return type == CameraType.Game && camera.CompareTag("MainCamera");
        }

        /// <summary>
        /// Кисть этой камеры действительно рисуется: рисовка включена, проход создан (шейдер есть) и камера
        /// подходит с учётом ручки «Scene». Иначе контур юнитов остаётся на своём обычном месте.
        /// </summary>
        public static bool Stylizes(Camera camera, CameraType type, CameraRenderType renderType) =>
            ComicStyle.Enabled && s_stylizeReady && AppliesTo(camera, type, renderType, s_sceneView);

        protected override void Dispose(bool disposing) => ReleaseMaterials();

        private void ReleaseMaterials()
        {
            CoreUtils.Destroy(_styleMaterial);
            CoreUtils.Destroy(_tiltMaterial);
            _styleMaterial = _tiltMaterial = null;
            _stylize = null;
            _tilt = null;
            s_stylizeReady = false;
        }

        // ---------------------------------------------------------------------------------------------
        // Полноэкранная отрисовка своим блоком свойств: у прохода до трёх входных текстур, а блок
        // копируется в командный буфер при записи вызова (как у Blitter).
        // ---------------------------------------------------------------------------------------------
        private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");

        private sealed class DrawData
        {
            public Material Material;
            public int Pass;
            public MaterialPropertyBlock Block;
            public int Id0, Id1, Id2, Id3;
            public TextureHandle Tex0, Tex1, Tex2, Tex3;
        }

        private static void AddDraw(RenderGraph graph, string name, Material material, int pass, MaterialPropertyBlock block,
            TextureHandle target, AccessFlags targetAccess,
            int id0, TextureHandle tex0, int id1 = 0, TextureHandle tex1 = default, int id2 = 0, TextureHandle tex2 = default,
            int id3 = 0, TextureHandle tex3 = default)
        {
            using (var builder = graph.AddRasterRenderPass<DrawData>(name, out var data))
            {
                data.Material = material;
                data.Pass = pass;
                data.Block = block;
                data.Id0 = id0; data.Tex0 = tex0;
                data.Id1 = id1; data.Tex1 = tex1;
                data.Id2 = id2; data.Tex2 = tex2;
                data.Id3 = id3; data.Tex3 = tex3;
                if (tex0.IsValid()) builder.UseTexture(tex0, AccessFlags.Read);
                if (tex1.IsValid()) builder.UseTexture(tex1, AccessFlags.Read);
                if (tex2.IsValid()) builder.UseTexture(tex2, AccessFlags.Read);
                if (tex3.IsValid()) builder.UseTexture(tex3, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, targetAccess);
                builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) =>
                {
                    d.Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    if (d.Tex0.IsValid()) d.Block.SetTexture(d.Id0, d.Tex0);
                    if (d.Tex1.IsValid()) d.Block.SetTexture(d.Id1, d.Tex1);
                    if (d.Tex2.IsValid()) d.Block.SetTexture(d.Id2, d.Tex2);
                    if (d.Tex3.IsValid()) d.Block.SetTexture(d.Id3, d.Tex3);
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, d.Pass, MeshTopology.Triangles, 3, 1, d.Block);
                });
            }
        }

        private static TextureDesc TargetDesc(int width, int height, GraphicsFormat format, string name, FilterMode filter)
        {
            return new TextureDesc(Mathf.Max(1, width), Mathf.Max(1, height))
            {
                format = format,
                name = name,
                msaaSamples = MSAASamples.None,
                bindTextureMS = false,
                clearBuffer = false,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        private static Vector4 Texel(int width, int height) =>
            new Vector4(1f / Mathf.Max(1, width), 1f / Mathf.Max(1, height), width, height);

        /// <summary>Размер кадра камеры: у промежуточной цели с render scale он меньше окна.</summary>
        private static void TargetSize(RenderGraph graph, TextureHandle color, UniversalCameraData cameraData, out int width, out int height)
        {
            var desc = graph.GetTextureDesc(color);
            if (desc.sizeMode == TextureSizeMode.Explicit && desc.width > 0 && desc.height > 0)
            {
                width = desc.width;
                height = desc.height;
                return;
            }
            width = cameraData.cameraTargetDescriptor.width;
            height = cameraData.cameraTargetDescriptor.height;
        }

        // ---------------------------------------------------------------------------------------------
        // 1. Кисть, тон и тушь: после непрозрачных и неба, до прозрачных.
        // ---------------------------------------------------------------------------------------------
        private sealed class StylizePass : ScriptableRenderPass
        {
            private const int NormalsShaderPass = 0, PaintShaderPass = 1, MixShaderPass = 2, InkShaderPass = 3, KeyShaderPass = 4,
                KeyGridShaderPass = 5;
            /// <summary>Сетка средней яркости кадра (как прежние 40×24 выборки одного потока).</summary>
            private const int KeyGridWidth = 40, KeyGridHeight = 24;

            private static readonly int DepthId = Shader.PropertyToID("_ComicDepth");
            private static readonly int NormalsId = Shader.PropertyToID("_ComicNormals");
            private static readonly int PaintedId = Shader.PropertyToID("_ComicPainted");
            private static readonly int TexelId = Shader.PropertyToID("_ComicTexel");
            private static readonly int SourceTexelId = Shader.PropertyToID("_ComicSourceTexel");
            private static readonly int CameraId = Shader.PropertyToID("_ComicCamera");
            private static readonly int ZBufferId = Shader.PropertyToID("_ComicZBuffer");
            private static readonly int ProjectionId = Shader.PropertyToID("_ComicProjection");
            private static readonly int PaintId = Shader.PropertyToID("_ComicPaint");
            private static readonly int InkColorId = Shader.PropertyToID("_ComicInkColor");
            private static readonly int InkId = Shader.PropertyToID("_ComicInk");
            private static readonly int InkSmallId = Shader.PropertyToID("_ComicInkSmall");
            private static readonly int EdgeId = Shader.PropertyToID("_ComicEdge");
            private static readonly int ToneId = Shader.PropertyToID("_ComicTone");
            private static readonly int ShadowTintId = Shader.PropertyToID("_ComicShadowTint");
            private static readonly int GradeId = Shader.PropertyToID("_ComicGrade");
            private static readonly int KeyId = Shader.PropertyToID("_ComicKey");
            private static readonly int KeyGridId = Shader.PropertyToID("_ComicKeyGrid");
            private static readonly int PaintWeightsId = Shader.PropertyToID("_ComicPaintWeights");
            private static readonly int ToneKeyId = Shader.PropertyToID("_ComicToneKey");

            private readonly Material _material;
            private readonly ComicStyleSettings _settings;
            private readonly MaterialPropertyBlock _normalsBlock = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock _paintBlock = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock _mixBlock = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock _inkBlock = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock _keyBlock = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock _keyGridBlock = new MaterialPropertyBlock();
            // Веса секторов кисти: считаются заново только при смене радиуса ядра (от разрешения).
            private readonly float[] _tapWeights = new float[ComicStyleRules.MaxKernelTaps * 8];
            private readonly Vector4[] _paintWeights = new Vector4[ComicStyleRules.MaxKernelTaps * 2];
            private int _weightsKernel = -1;
            private Vector4 _cameraParams;

            public StylizePass(Material material, ComicStyleSettings settings)
            {
                _material = material;
                _settings = settings;
                // После неба, но до копии _CameraOpaqueTexture и до прозрачных: вода с преломлением видит
                // уже нарисованный мир, а эффекты, метки и полоски ложатся поверх чистыми.
                renderPassEvent = StylizeEvent;
                // Глубину игра и так просит (PC_RPAsset, SSAO); нормалей НЕ просим — см. описание фичи.
                ConfigureInput(ScriptableRenderPassInput.Depth);
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var cameraData = frameData.Get<UniversalCameraData>();
                TextureHandle color = resources.activeColorTexture;
                TextureHandle depth = resources.cameraDepthTexture;
                if (!color.IsValid() || !depth.IsValid()) return;

                var s = _settings;
                TargetSize(graph, color, cameraData, out int width, out int height);
                GraphicsFormat colorFormat = graph.GetTextureDesc(color).format;
                Vector4 texel = Texel(width, height);
                SetCamera(cameraData.camera, texel);

                // Нормали из глубины: хватает 8 бит — пороги излома от 30°.
                TextureHandle normals = graph.CreateTexture(TargetDesc(width, height, GraphicsFormat.R8G8B8A8_UNorm,
                    "Comic normals", FilterMode.Point));
                AddDraw(graph, "Comic normals", _material, NormalsShaderPass, _normalsBlock, normals, AccessFlags.WriteAll,
                    DepthId, depth);

                // Средняя яркость мира (log2) в одном пикселе: по ней ложатся ступени тона. Считается до
                // прозрачных — вспышки эффектов не сдвигают ступени; фон за краем поляны не считается.
                TextureHandle key = TextureHandle.nullHandle;
                if (s.ToneAutoPivot && s.ToneStrength + s.ShadowWarmth > 0f)
                {
                    TextureHandle grid = graph.CreateTexture(TargetDesc(KeyGridWidth, KeyGridHeight,
                        GraphicsFormat.R16G16B16A16_SFloat, "Comic key grid", FilterMode.Point));
                    _keyGridBlock.SetVector(CameraId, _cameraParams);
                    AddDraw(graph, "Comic key grid", _material, KeyGridShaderPass, _keyGridBlock, grid, AccessFlags.WriteAll,
                        BlitTextureId, color, DepthId, depth);
                    key = graph.CreateTexture(TargetDesc(1, 1, GraphicsFormat.R16G16B16A16_SFloat, "Comic key", FilterMode.Point));
                    AddDraw(graph, "Comic key", _material, KeyShaderPass, _keyBlock, key, AccessFlags.WriteAll,
                        KeyGridId, grid);
                }

                // Кисть: готовая «живописная» копия кадра полного разрешения.
                int kernel = ComicStyleRules.PainterlyKernel(s.PainterlyRadius, s.PainterlyStrength, height,
                    s.PainterlyHalfResolution, out bool half);
                TextureHandle painted = graph.CreateTexture(TargetDesc(width, height, colorFormat, "Comic painted", FilterMode.Bilinear));
                if (kernel > 0) SetPaintWeights(kernel);
                if (kernel <= 0)
                {
                    _paintBlock.SetVector(SourceTexelId, texel);
                    _paintBlock.SetVector(PaintId, new Vector4(0f, 0f, s.PainterlySharpness, s.PainterlyHardness));
                    AddDraw(graph, "Comic paint (copy)", _material, PaintShaderPass, _paintBlock, painted, AccessFlags.WriteAll,
                        BlitTextureId, color);
                }
                else if (!half)
                {
                    _paintBlock.SetVector(SourceTexelId, texel);
                    _paintBlock.SetVector(PaintId, new Vector4(kernel, s.PainterlyStrength, s.PainterlySharpness, s.PainterlyHardness));
                    AddDraw(graph, "Comic paint", _material, PaintShaderPass, _paintBlock, painted, AccessFlags.WriteAll,
                        BlitTextureId, color);
                }
                else
                {
                    int hw = Mathf.Max(1, width / 2), hh = Mathf.Max(1, height / 2);
                    TextureHandle halfPaint = graph.CreateTexture(TargetDesc(hw, hh, colorFormat, "Comic paint half", FilterMode.Bilinear));
                    // Ядро в текселях половинного кадра: шаг выборки — два пикселя исходника.
                    _paintBlock.SetVector(SourceTexelId, new Vector4(2f * texel.x, 2f * texel.y, hw, hh));
                    _paintBlock.SetVector(PaintId, new Vector4(kernel, 1f, s.PainterlySharpness, s.PainterlyHardness));
                    AddDraw(graph, "Comic paint (half)", _material, PaintShaderPass, _paintBlock, halfPaint, AccessFlags.WriteAll,
                        BlitTextureId, color);
                    _mixBlock.SetVector(PaintId, new Vector4(kernel, s.PainterlyStrength, 0f, 0f));
                    AddDraw(graph, "Comic paint mix", _material, MixShaderPass, _mixBlock, painted, AccessFlags.WriteAll,
                        BlitTextureId, color, PaintedId, halfPaint);
                }

                // Тон и тушь — обратно в цель камеры (MSAA-цель принимает полноэкранную запись целиком).
                float inkPixels = ComicStyleRules.OutlinePixels(s.OutlineThickness, height);
                int rings = ComicStyleRules.InkRings(inkPixels);
                // Свет арены (ComicStyleMood) смешивает тушь, тени и тон со своей целью; без него — ровно настройки.
                Color ink = ComicStyleMood.InkColor(s.InkColor).linear;
                _inkBlock.SetVector(InkColorId, new Vector4(ink.r, ink.g, ink.b, ComicStyleMood.InkOpacity(s.InkOpacity)));
                _inkBlock.SetVector(InkId, new Vector4(inkPixels, rings, s.DepthThreshold, s.DepthSoftness));
                _inkBlock.SetVector(InkSmallId, new Vector4(s.SmallDepthThreshold, s.SmallDepthSoftness, 0f, 0f));
                float cosA = Mathf.Cos(s.NormalThreshold * Mathf.Deg2Rad);
                float cosB = Mathf.Cos(Mathf.Min(179f, s.NormalThreshold + s.NormalSoftness) * Mathf.Deg2Rad);
                _inkBlock.SetVector(EdgeId, new Vector4(cosA, Mathf.Max(1e-3f, cosA - cosB), s.ColourGateLow, s.ColourGateHigh));
                _inkBlock.SetVector(ToneId, new Vector4(s.ToneStrength, s.ToneBandStops, s.TonePivot, s.ToneHardness));
                _inkBlock.SetVector(ToneKeyId, new Vector4(key.IsValid() ? 1f : 0f, ComicStyleMood.TonePivotScale(s.TonePivotScale),
                    ComicStyleRules.MinTonePivot, ComicStyleRules.MaxTonePivot));
                if (!key.IsValid()) _inkBlock.SetTexture(KeyId, Texture2D.blackTexture);
                Color tint = ComicStyleMood.ShadowTint(s.ShadowTint).linear;
                float tintLuminance = Mathf.Max(1e-3f, tint.r * .2126f + tint.g * .7152f + tint.b * .0722f);
                _inkBlock.SetVector(ShadowTintId, new Vector4(tint.r / tintLuminance, tint.g / tintLuminance, tint.b / tintLuminance, ComicStyleMood.ShadowWarmth(s.ShadowWarmth)));
                _inkBlock.SetVector(GradeId, new Vector4(s.Saturation, s.ShadowSaturation, s.Brightness, s.HighlightKnee));
                AddDraw(graph, "Comic ink and tone", _material, InkShaderPass, _inkBlock, color, AccessFlags.Write,
                    BlitTextureId, painted, NormalsId, normals, DepthId, depth, KeyId, key);
            }

            private void SetPaintWeights(int kernel)
            {
                if (kernel != _weightsKernel)
                {
                    int taps = ComicStyleRules.PainterlyTapWeights(kernel, _tapWeights);
                    System.Array.Clear(_paintWeights, 0, _paintWeights.Length);
                    for (int i = 0; i < taps; i++)
                    {
                        int w = i * 8;
                        _paintWeights[2 * i] = new Vector4(_tapWeights[w], _tapWeights[w + 1], _tapWeights[w + 2], _tapWeights[w + 3]);
                        _paintWeights[2 * i + 1] = new Vector4(_tapWeights[w + 4], _tapWeights[w + 5], _tapWeights[w + 6], _tapWeights[w + 7]);
                    }
                    _weightsKernel = kernel;
                }
                // Массив всегда полной длины: у блока свойств длина массива фиксируется первой записью.
                _paintBlock.SetVectorArray(PaintWeightsId, _paintWeights);
            }

            private void SetCamera(Camera camera, Vector4 texel)
            {
                float near = camera.nearClipPlane, far = Mathf.Max(camera.farClipPlane, near + 1e-3f);
                bool ortho = camera.orthographic;
                // Параметры линейной глубины считаются здесь: глобальные _ZBufferParams не нужны.
                float ratio = far / Mathf.Max(1e-4f, near);
                Vector4 zBuffer = SystemInfo.usesReversedZBuffer
                    ? new Vector4(-1f + ratio, 1f, (-1f + ratio) / far, 1f / far)
                    : new Vector4(1f - ratio, ratio, (1f - ratio) / far, ratio / far);
                Matrix4x4 projection = camera.projectionMatrix;
                var projectionParams = new Vector4(1f / Mathf.Max(1e-6f, Mathf.Abs(projection.m00)),
                    1f / Mathf.Max(1e-6f, Mathf.Abs(projection.m11)), 0f, 0f);
                var cameraParams = new Vector4(near, far, ortho ? 1f : 0f, SystemInfo.usesReversedZBuffer ? 1f : 0f);
                _cameraParams = cameraParams;
                SetCameraBlock(_normalsBlock, texel, cameraParams, zBuffer, projectionParams);
                SetCameraBlock(_inkBlock, texel, cameraParams, zBuffer, projectionParams);
                _mixBlock.SetVector(TexelId, texel);
            }

            private static void SetCameraBlock(MaterialPropertyBlock block, Vector4 texel, Vector4 cameraParams,
                Vector4 zBuffer, Vector4 projectionParams)
            {
                block.SetVector(TexelId, texel);
                block.SetVector(CameraId, cameraParams);
                block.SetVector(ZBufferId, zBuffer);
                block.SetVector(ProjectionId, projectionParams);
            }
        }

        // ---------------------------------------------------------------------------------------------
        // 2. Диорама: мягкое размытие у верхней и нижней кромки, после пост-обработки, до HUD.
        // ---------------------------------------------------------------------------------------------
        private sealed class TiltShiftPass : ScriptableRenderPass
        {
            private const int DownsampleShaderPass = 0, BlurShaderPass = 1, CompositeShaderPass = 2;

            private static readonly int SourceTexelId = Shader.PropertyToID("_TiltSourceTexel");
            private static readonly int StepId = Shader.PropertyToID("_TiltStep");
            private static readonly int ShapeId = Shader.PropertyToID("_TiltShape");

            private readonly Material _material;
            private readonly ComicStyleSettings _settings;
            private readonly MaterialPropertyBlock _downBlock = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock _blurXBlock = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock _blurYBlock = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock _compositeBlock = new MaterialPropertyBlock();

            public TiltShiftPass(Material material, ComicStyleSettings settings)
            {
                _material = material;
                _settings = settings;
                // До финального блита и до Screen Space Overlay: HUD рисуется позже и остаётся резким.
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var cameraData = frameData.Get<UniversalCameraData>();
                TextureHandle color = resources.activeColorTexture;
                if (!color.IsValid()) return;

                var s = _settings;
                TargetSize(graph, color, cameraData, out int width, out int height);
                GraphicsFormat format = graph.GetTextureDesc(color).format;
                int qw = Mathf.Max(1, width / ComicStyleRules.TiltDownsample);
                int qh = Mathf.Max(1, height / ComicStyleRules.TiltDownsample);
                TextureHandle a = graph.CreateTexture(TargetDesc(qw, qh, format, "Comic tilt A", FilterMode.Bilinear));
                TextureHandle b = graph.CreateTexture(TargetDesc(qw, qh, format, "Comic tilt B", FilterMode.Bilinear));

                _downBlock.SetVector(SourceTexelId, Texel(width, height));
                AddDraw(graph, "Comic tilt downsample", _material, DownsampleShaderPass, _downBlock, a, AccessFlags.WriteAll,
                    BlitTextureId, color);

                // Девять выборок на ±2σ: шаг — половина сигмы в текселях четверти.
                float sigma = ComicStyleRules.TiltBlurSigma(s.TiltBlur, height);
                float spacing = Mathf.Max(.5f, sigma * .5f);
                _blurXBlock.SetVector(StepId, new Vector4(spacing / qw, 0f, 0f, 0f));
                AddDraw(graph, "Comic tilt blur X", _material, BlurShaderPass, _blurXBlock, b, AccessFlags.WriteAll,
                    BlitTextureId, a);
                _blurYBlock.SetVector(StepId, new Vector4(0f, spacing / qh, 0f, 0f));
                AddDraw(graph, "Comic tilt blur Y", _material, BlurShaderPass, _blurYBlock, a, AccessFlags.WriteAll,
                    BlitTextureId, b);

                // Смешивание альфой прямо в кадр: резкий центр не копируется вовсе.
                _compositeBlock.SetVector(ShapeId, new Vector4(s.TiltSharpBand, ComicStyleMood.TiltStrength(s.TiltStrength), 0f, 0f));
                AddDraw(graph, "Comic tilt composite", _material, CompositeShaderPass, _compositeBlock, color, AccessFlags.ReadWrite,
                    BlitTextureId, a);
            }
        }
    }
}
