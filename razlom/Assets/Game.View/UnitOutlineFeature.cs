using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>Тонкий контур видимого силуэта, независимый от швов и нормалей меша.</summary>
    public sealed class UnitOutlineFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader _compositeShader;
        private Material _material;
        private OutlinePass _pass;

        public void SetShader(Shader shader) => _compositeShader = shader;

        public override void Create()
        {
            CoreUtils.Destroy(_material);
            if (_compositeShader == null)
                _compositeShader = Shader.Find("Hidden/Razlom/Unit Outline");
            if (_compositeShader == null) return;
            _material = CoreUtils.CreateEngineMaterial(_compositeShader);
            _pass = new OutlinePass(_material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass != null && renderingData.cameraData.cameraType != CameraType.Preview)
                renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(_material);

        private sealed class OutlinePass : ScriptableRenderPass
        {
            private static readonly ShaderTagId MaskTag = new ShaderTagId("UnitOutlineMask");
            private readonly Material _composite;

            public OutlinePass(Material composite)
            {
                _composite = composite;
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            private sealed class MaskData { public RendererListHandle Renderers; }
            private sealed class CompositeData
            {
                public TextureHandle Mask;
                public Material Material;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                // Внешняя RenderTexture портрета может быть back buffer без дескриптора графа.
                if (resources.isActiveTargetBackBuffer) return;
                var camera = frameData.Get<UniversalCameraData>();
                var rendering = frameData.Get<UniversalRenderingData>();
                var lights = frameData.Get<UniversalLightData>();

                // Число MSAA-сэмплов должно совпадать с глубиной камеры.
                // Маска очищается отдельно: глубина мира остаётся нетронутой.
                var desc = graph.GetTextureDesc(resources.activeColorTexture);
                desc.name = "Unit silhouettes";
                desc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                desc.clearBuffer = true;
                desc.clearColor = Color.clear;
                desc.bindTextureMS = false;
                var mask = graph.CreateTexture(desc);
                var drawing = RenderingUtils.CreateDrawingSettings(MaskTag, rendering, camera,
                    lights, camera.defaultOpaqueSortFlags);
                var filtering = new FilteringSettings(RenderQueueRange.opaque);
                var list = graph.CreateRendererList(new RendererListParams(rendering.cullResults,
                    drawing, filtering));

                using (var builder = graph.AddRasterRenderPass<MaskData>("Unit outline mask", out var data))
                {
                    data.Renderers = list;
                    builder.UseRendererList(list);
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc(static (MaskData pass, RasterGraphContext context) =>
                        context.cmd.DrawRendererList(pass.Renderers));
                }

                using (var builder = graph.AddRasterRenderPass<CompositeData>("Unit silhouette ink", out var data))
                {
                    data.Mask = mask;
                    data.Material = _composite;
                    builder.UseTexture(mask, AccessFlags.Read);
                    // Альфа-смешивание сохраняет исходный кадр без его копирования.
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, pass.Mask, new Vector4(1, 1, 0, 0), pass.Material, 0));
                }
            }
        }
    }
}
