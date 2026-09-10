using Game.View;
using UnityEditor;
using UnityEngine;

public static class PelagSquallVfxSetup
{
    private const string Folder = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Sword Trails/Plain/";
    private const string MaterialPath = "Assets/Resources/VFX/Pelag/Materials/M_Pelag_Squall_Dash.mat";
    private const string MeshPath = "Assets/Resources/VFX/Pelag/Geometry/SquallDash.asset";

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Install;

    [MenuItem("Разлом/Pelag VFX/Подключить авторский Шквал")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library = AssetDatabase.LoadAssetAtPath<AbilityVfxLibrary>("Assets/Resources/VFX/Pelag/AbilityVfxLibrary.asset");
        if (library == null) return;
        bool configureDash = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) == null;
        if (configureDash)
        {
            var mesh = new Mesh { name = "SquallDash" };
            const int segments = 32;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float width = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), .8f) * .06f;
                vertices[2 * i] = new Vector3(Mathf.Lerp(-.95f, .25f, t), -width, 0);
                vertices[2 * i + 1] = new Vector3(vertices[2 * i].x, width, 0);
                uv[2 * i] = new Vector2(t, 0);
                uv[2 * i + 1] = new Vector2(t, 1);
                if (i == segments) continue;
                int v = i * 2, at = i * 6;
                triangles[at] = v; triangles[at + 1] = v + 1; triangles[at + 2] = v + 2;
                triangles[at + 3] = v + 1; triangles[at + 4] = v + 3; triangles[at + 5] = v + 2;
            }
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
            mesh.RecalculateBounds(); mesh.RecalculateNormals();
            AssetDatabase.CreateAsset(mesh, MeshPath);
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr sword trail plain.mat");
            var material = new Material(source) { name = "M_Pelag_Squall_Dash" };
            material.DisableKeyword("_CFXR_DISSOLVE");
            material.DisableKeyword("_CFXR_DISSOLVE_ALONG_UV_X");
            material.SetFloat("_UseDissolve", 0);
            material.SetFloat("_UseDissolveOffsetUV", 0);
            material.SetFloat("_DoubleDissolve", 0);
            AssetDatabase.CreateAsset(material, MaterialPath);
            var root = PrefabUtility.LoadPrefabContents(Folder + "VFX_Pelag_Squall_Dash.prefab");
            try
            {
                int index = 0;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    renderer.mesh = mesh; renderer.sharedMaterial = material;
                    renderer.alignment = ParticleSystemRenderSpace.Local;
                    var main = ps.main; main.startRotation = 0f;
                    var custom = ps.customData; custom.enabled = false;
                    if (ps.gameObject != root)
                        ps.transform.localPosition = new Vector3(-.08f * index, index == 1 ? .16f : -.16f, 0);
                    index++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "VFX_Pelag_Squall_Dash.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Bind(library, PelagVfxId.ChainStepDash, "Dash", .2f);
        Bind(library, PelagVfxId.ChainStepHit, "Hit", .22f);
        Bind(library, PelagVfxId.ChainStepFinish, "Finish", .25f);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    private static void Bind(AbilityVfxLibrary library, PelagVfxId id, string suffix, float lifetime)
    {
        string path = Folder + "VFX_Pelag_Squall_" + suffix + ".prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return;
        var element = prefab.GetComponent<PelagVfxElement>();
        if (element == null || element.Id != id)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                element = root.GetComponent<PelagVfxElement>();
                if (element == null) element = root.AddComponent<PelagVfxElement>();
                element.Id = id; element.DefaultLifetime = lifetime;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        int index = System.Array.FindIndex(library.Entries, entry => entry.Id == id);
        if (index >= 0 && library.Entries[index].Prefab == prefab) return;
        if (index < 0) { index = library.Entries.Length; System.Array.Resize(ref library.Entries, index + 1); }
        library.Entries[index] = new AbilityVfxLibrary.Entry { Id = id, Prefab = prefab, Prewarm = 6 };
        EditorUtility.SetDirty(library);
    }
}
