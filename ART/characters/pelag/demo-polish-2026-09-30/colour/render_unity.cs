var output = "C:/Users/d.grab/Desktop/the-game/ART/characters/pelag/demo-polish-2026-09-30/colour";
System.IO.Directory.CreateDirectory(output);
var preview = new UnityEditor.PreviewRenderUtility();
var root = new UnityEngine.GameObject("Pelag_ColorPreview") { hideFlags=UnityEngine.HideFlags.HideAndDontSave };
preview.AddSingleGO(root);
try
{
    var factory = root.AddComponent<Game.View.ArenaView>();
    var body = factory.CreateCampPlayer(); body.transform.SetParent(root.transform,false);
    var animator = body.GetComponent<UnityEngine.Animator>(); animator.Update(0f);
    preview.camera.orthographic=true; preview.camera.orthographicSize=1.13f;
    preview.camera.nearClipPlane=.01f; preview.camera.farClipPlane=30;
    preview.camera.clearFlags=UnityEngine.CameraClearFlags.SolidColor;
    preview.camera.backgroundColor=new UnityEngine.Color(.12f,.15f,.14f,1);
    preview.ambientColor=new UnityEngine.Color(.55f,.55f,.55f);
    preview.lights[0].intensity=1.2f; preview.lights[0].transform.rotation=UnityEngine.Quaternion.Euler(50,-30,0);
    preview.lights[1].intensity=.35f; preview.lights[1].transform.rotation=UnityEngine.Quaternion.Euler(35,150,0);
    foreach (var pair in new[] { new {n="after-front",y=180f,p=12f}, new {n="after-game",y=135f,p=40f},
        new {n="after-back",y=0f,p=12f}, new {n="after-side",y=90f,p=5f}, new {n="after-back-game",y=35f,p=40f} })
    {
        preview.camera.transform.rotation=UnityEngine.Quaternion.Euler(pair.p,pair.y,0);
        preview.camera.transform.position=new UnityEngine.Vector3(0,.98f,0)-preview.camera.transform.forward*8f;
        preview.BeginPreview(new UnityEngine.Rect(0,0,1000,1000),UnityEngine.GUIStyle.none); preview.Render(true);
        var tex=preview.EndPreview(); var previous=UnityEngine.RenderTexture.active;
        var rt=UnityEngine.RenderTexture.GetTemporary(1000,1000,0,UnityEngine.RenderTextureFormat.ARGB32);
        UnityEngine.Graphics.Blit(tex,rt); UnityEngine.RenderTexture.active=rt;
        var png=new UnityEngine.Texture2D(1000,1000,UnityEngine.TextureFormat.RGB24,false);
        png.ReadPixels(new UnityEngine.Rect(0,0,1000,1000),0,0); png.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,pair.n+".png"),png.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(png); UnityEngine.RenderTexture.active=previous;
        UnityEngine.RenderTexture.ReleaseTemporary(rt);
    }
    var equipment=body.GetComponent<Game.View.PelagEquipmentView>();
    var anchor=equipment.SlamHead;
    var mesh=anchor.GetComponentInChildren<UnityEngine.MeshFilter>().sharedMesh;
    var report=new { triangles=mesh.triangles.Length/3, meshBounds=mesh.bounds.ToString(),
        position=anchor.position.ToString("F5"), rotation=anchor.rotation.eulerAngles.ToString("F5"),
        scale=anchor.lossyScale.ToString("F5"), parent=anchor.parent.name,
        headRenderBounds=anchor.GetComponentInChildren<UnityEngine.Renderer>().bounds.ToString(),
        mounts=body.GetComponentsInChildren<UnityEngine.Transform>(true).Where(t=>t.name.Contains("AnchorGrip"))
            .Select(t=>new{t.name,position=t.position.ToString("F5"),rotation=t.rotation.eulerAngles.ToString("F5"),
                bounds=t.GetComponentInChildren<UnityEngine.Renderer>().bounds.ToString()}).ToArray(),
        bodyHeight=body.GetComponentInChildren<UnityEngine.SkinnedMeshRenderer>().bounds.size.y,
        sceneDirty=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty };
    return report;
}
finally { preview.Cleanup(); if(root!=null) UnityEngine.Object.DestroyImmediate(root); }
