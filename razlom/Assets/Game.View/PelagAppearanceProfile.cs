using UnityEngine;

namespace Game.View
{
    /// <summary>Editable colour and back-mounted weapon settings, independent of the hero rig.</summary>
    [CreateAssetMenu(menuName = "Разлом/Пелаг/Внешность", fileName = "PelagAppearance")]
    public sealed class PelagAppearanceProfile : ScriptableObject
    {
        public const string ResourcePath = "Characters/Pelag_v6/PelagAppearance";
        public Material BodyMaterial;
        public GameObject AnchorHeadPrefab;
        public Material AnchorHeadMaterial;
        public AnchorHeadShape AnchorShape;
        public string BackSocket = "mixamorig:Spine2";
        public Vector3 HeadPosition = new Vector3(.008f, -.031f, -.075f);
        public Vector3 HeadRotation = new Vector3(0f, 0f, -25f);
        [Min(.1f)] public float HeadSize = .94f;
        public Vector3 GripPosition = new Vector3(.103f, .144f, -.105f);
        public Vector3 GripRotation = new Vector3(-90f, 0f, 90f);
        public Vector3 GripScale = new Vector3(.55f, .55f, .55f);

        // Capture-only switch gives a repeatable comparison inside the same player build.
        private static readonly bool CaptureOriginal = System.Array.IndexOf(
            System.Environment.GetCommandLineArgs(), "-capture-pelag-original-look") >= 0;
        public static PelagAppearanceProfile Load() => CaptureOriginal ? null
            : Resources.Load<PelagAppearanceProfile>(ResourcePath);
    }
}
