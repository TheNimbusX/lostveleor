var output = "C:/Users/d.grab/Desktop/the-game/ART/characters/pelag/demo-polish-2026-09-30/colour/poses";
System.IO.Directory.CreateDirectory(output);
var preview = new UnityEditor.PreviewRenderUtility();
var root = new UnityEngine.GameObject("Pelag_PoseCheck") { hideFlags=UnityEngine.HideFlags.HideAndDontSave };
preview.AddSingleGO(root);
try {
    var factory=root.AddComponent<Game.View.ArenaView>(); var body=factory.CreateCampPlayer();
    body.transform.SetParent(root.transform,false); var animator=body.GetComponent<UnityEngine.Animator>(); animator.Update(0);
    var clips=animator.runtimeAnimatorController.animationClips;
    preview.camera.orthographic=true;preview.camera.orthographicSize=1.3f;
    preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=30;
    preview.camera.clearFlags=UnityEngine.CameraClearFlags.SolidColor;
    preview.camera.backgroundColor=new UnityEngine.Color(.12f,.15f,.14f);
    preview.ambientColor=new UnityEngine.Color(.55f,.55f,.55f);
    preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=UnityEngine.Quaternion.Euler(50,-30,0);
    preview.lights[1].intensity=.35f;preview.lights[1].transform.rotation=UnityEngine.Quaternion.Euler(35,150,0);
    var samples=new[] { new{name="Pelag_MX_Run",t=.18f},new{name="Pelag_AN_Roll",t=.28f},
        new{name="Pelag_AN_Cleave",t=.48f},new{name="Pelag_Whirlwind_Timed",t=.4f},
        new{name="Pelag_Saber_A_Timed",t=.3f},new{name="Pelag_AN_Skewer",t=.14f},
        new{name="Pelag_AN_Backblast",t=.14f},new{name="Pelag_AN_FireFlask",t=.32f},
        new{name="Pelag_Blaze_Pour",t=.8f},new{name="Pelag_AN_SquallA",t=.14f},
        new{name="Pelag_AN_AnchorSlam",t=.5f},new{name="Pelag_AN_AnchorLeap",t=.6f},
        new{name="Pelag_AN_WreckA",t=.4f},new{name="Pelag_MX_Death",t=2.4f} };
    var report=new System.Collections.Generic.List<object>();
    foreach(var sample in samples) {
        animator.Update(0); var clip=clips.First(c=>c.name==sample.name); clip.SampleAnimation(body,sample.t);
        body.transform.localScale=UnityEngine.Vector3.one*1.82f;
        body.transform.localPosition=UnityEngine.Vector3.zero;body.transform.localRotation=UnityEngine.Quaternion.identity;
        var equipment=body.GetComponent<Game.View.PelagEquipmentView>();var head=equipment.SlamHead;
        var renders=body.GetComponentsInChildren<UnityEngine.Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
        var b=renders[0].bounds;foreach(var r in renders)b.Encapsulate(r.bounds);
        preview.camera.transform.rotation=UnityEngine.Quaternion.Euler(15,35,0);
        preview.camera.transform.position=b.center-preview.camera.transform.forward*8f;
        preview.camera.orthographicSize=UnityEngine.Mathf.Max(1.15f,b.extents.magnitude*.9f);
        preview.BeginPreview(new UnityEngine.Rect(0,0,800,800),UnityEngine.GUIStyle.none);preview.Render(true);var tex=preview.EndPreview();
        var previous=UnityEngine.RenderTexture.active;var rt=UnityEngine.RenderTexture.GetTemporary(800,800,0);UnityEngine.Graphics.Blit(tex,rt);UnityEngine.RenderTexture.active=rt;
        var png=new UnityEngine.Texture2D(800,800,UnityEngine.TextureFormat.RGB24,false);png.ReadPixels(new UnityEngine.Rect(0,0,800,800),0,0);png.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,sample.name+".png"),png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);
        UnityEngine.RenderTexture.active=previous;UnityEngine.RenderTexture.ReleaseTemporary(rt);
        report.Add(new{sample.name,sample.t,storedHeadPosition=head.localPosition.ToString("F5"),storedHeadParent=head.parent.name,
            distanceToReturn=UnityEngine.Vector3.Distance(head.position,equipment.SlamBeltPosition)});
    }
    return report;
} finally { preview.Cleanup(); if(root!=null) UnityEngine.Object.DestroyImmediate(root); }
