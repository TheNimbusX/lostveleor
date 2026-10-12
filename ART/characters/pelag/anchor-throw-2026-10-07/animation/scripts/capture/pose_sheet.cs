// Бросок якоря 07.10 — позы клипов AnchorThrow2 в редакторе без Play (память unity-editmode-pose-render): сцена-превью,
// модель Pelag_v6, SampleAnimation + BakeMesh без родителя, камера как у листа поз (правый бок, довёрнута на 30° вперёд).
// Кадры — artifacts/throw-0710/poses/<n>_<clip>_<frame>.png; лист собирает tools/pose_sheet.py.
var outDir = @"C:\Users\d.grab\Desktop\the-game\artifacts\throw-0710\poses";
System.IO.Directory.CreateDirectory(outDir);
var shots = new (string clip, float frame, string tag)[] {
    ("Throw", 1f, "1_t01"), ("Throw", 2f, "2_t02"), ("Throw", 5f, "3_t05"), ("Yank", 0f, "4_t10"),
    ("Haul", 1f, "5_t13"), ("Catch", 0f, "6_t17"), ("Catch", 6f, "7_t23") };
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var made = new System.Collections.Generic.List<UnityEngine.Object>();
var log = new System.Text.StringBuilder();
UnityEngine.RenderTexture rt = null;
UnityEngine.Camera cam = null;
try
{
    var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx");
    var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Resources/Characters/Pelag_v6/Pelag_ColorPolish.mat");
    var body = UnityEngine.Object.Instantiate(model); made.Add(body);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(body, scene);
    foreach (var a in body.GetComponentsInChildren<UnityEngine.Animator>()) a.enabled = false;
    var smr = body.GetComponentInChildren<UnityEngine.SkinnedMeshRenderer>();
    var baked = new UnityEngine.Mesh(); made.Add(baked);
    var show = new UnityEngine.GameObject("baked"); made.Add(show);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(show, scene);
    show.AddComponent<UnityEngine.MeshFilter>().sharedMesh = baked;
    var mr = show.AddComponent<UnityEngine.MeshRenderer>();
    var mats = smr.sharedMaterials;
    if (mat != null) { mats = new UnityEngine.Material[smr.sharedMaterials.Length]; for (int i = 0; i < mats.Length; i++) mats[i] = mat; }
    mr.sharedMaterials = mats;
    smr.enabled = false;
    var lightGo = new UnityEngine.GameObject("key"); made.Add(lightGo);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
    var light = lightGo.AddComponent<UnityEngine.Light>(); light.type = UnityEngine.LightType.Directional; light.intensity = 1.4f;
    lightGo.transform.rotation = UnityEngine.Quaternion.Euler(40f, -60f, 0f);
    var camGo = new UnityEngine.GameObject("cam"); made.Add(camGo);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
    cam = camGo.AddComponent<UnityEngine.Camera>();
    cam.scene = scene; cam.orthographic = true; cam.orthographicSize = .62f;
    cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor; cam.backgroundColor = new UnityEngine.Color(.87f, .87f, .85f, 1f);
    cam.nearClipPlane = .01f; cam.farClipPlane = 50f;
    rt = new UnityEngine.RenderTexture(560, 640, 24); cam.targetTexture = rt;
    var bones = new System.Collections.Generic.Dictionary<string, UnityEngine.Transform>();
    foreach (var t in body.GetComponentsInChildren<UnityEngine.Transform>()) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
    float scale = body.transform.lossyScale.y;
    foreach (var s in shots)
    {
        var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AnimationClip>("Assets/Resources/Characters/Pelag_v5/Pelag_AN_AnchorThrow2_" + s.clip + ".anim");
        clip.SampleAnimation(body, s.frame / 30f);
        smr.BakeMesh(baked);
        show.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
        show.transform.localScale = UnityEngine.Vector3.one;
        var b = baked.bounds;
        // центр кадра — середина тела по высоте над стопами, по горизонтали — таз
        var hips = bones["mixamorig:Hips"].position;
        float ground = UnityEngine.Mathf.Min(bones["mixamorig:LeftToe_End"].position.y, bones["mixamorig:RightToe_End"].position.y);
        float head = bones["mixamorig:HeadTop_End"].position.y;
        float h = head - ground;
        cam.orthographicSize = h * .64f;
        var wb = mr.bounds;
        var center = new UnityEngine.Vector3(wb.center.x, ground + h * .5f, wb.center.z);
        // герой смотрит в +Z, правый бок — +X: камера справа, довёрнута на 30° вперёд и чуть сверху
        var dir = (UnityEngine.Quaternion.Euler(0f, -30f, 0f) * new UnityEngine.Vector3(-1f, 0f, 0f));
        dir = (dir + new UnityEngine.Vector3(0f, -.1f, 0f)).normalized;
        cam.transform.position = center - dir * 10f * h;
        cam.transform.rotation = UnityEngine.Quaternion.LookRotation(dir, UnityEngine.Vector3.up);
        cam.Render();
        var prev = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt;
        var tex = new UnityEngine.Texture2D(rt.width, rt.height, UnityEngine.TextureFormat.RGB24, false);
        tex.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        UnityEngine.RenderTexture.active = prev;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, s.tag + "_" + s.clip + "_" + s.frame + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        log.Append(s.tag + " ok h=" + h.ToString("F4") + "\n");
    }
}
finally
{
    if (cam != null) cam.targetTexture = null;
    if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
    foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
}
return log.ToString();
