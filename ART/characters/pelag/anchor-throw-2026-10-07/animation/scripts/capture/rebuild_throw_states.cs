// Пересобрать клипы Броска (AnchorThrow2) из обновлённых FBX: импорт FBX, AddAnchorThrowStates (Build + опоры на землю),
// сохранение только своего (контроллер и 5 .anim) — SaveAnchorThrowAssets, без общего SaveAssets.
var t = System.Type.GetType("RazlomPelagV5AnimatorBuilder, Assembly-CSharp-Editor");
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
var files = (string[])t.GetMethod("AnchorThrowFiles", flags).Invoke(null, null);
t.GetMethod("ForceImport", flags).Invoke(null, new object[] { files });
var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/Resources/Characters/Pelag_v5/Pelag_v5_FullCombat.controller");
bool ok = (bool)t.GetMethod("AddAnchorThrowStates", flags).Invoke(null, new object[] { controller.layers[0].stateMachine });
if (ok) { UnityEditor.EditorUtility.SetDirty(controller); t.GetMethod("SaveAnchorThrowAssets", flags).Invoke(null, new object[] { controller }); }
var sb = new System.Text.StringBuilder("ok=" + ok + "\n");
foreach (var s in controller.layers[0].stateMachine.states) if (s.state.name.StartsWith("AnchorThrow")) sb.Append(s.state.name + " -> " + (s.state.motion != null ? s.state.motion.name : "null") + "\n");
return sb.ToString();
