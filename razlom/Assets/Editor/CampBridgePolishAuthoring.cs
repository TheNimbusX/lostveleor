using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Game.EditorTools
{
    public static class CampBridgePolishAuthoring
    {
        public const string Folder = "Assets/Resources/Environment/Camp/BridgePolish";
        const string JournalPath = "Assets/Editor/CampBridgePolishData/Journal.asset";
        const string StonesName = "Bridge polish — submerged stones";
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/camp-bridge-polish-2026-10-05"));
        static readonly string[] RiverMeshes = { "Ближний берег", "Дно реки", "Дальний берег" };

        static CampRiver River()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Apply the visual pass outside Play Mode.");
            var river = Object.FindAnyObjectByType<CampRiver>();
            if (river == null || river.gameObject.scene.path != "Assets/Scenes/SampleScene.unity")
                throw new InvalidOperationException("Open the authored camp scene.");
            if (river.Geometry == null || river.GetComponent<CampRiverPassage>()?.Bridge == null)
                throw new InvalidOperationException("Existing river geometry and bridge are required.");
            return river;
        }

        public static string ScenePath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
        static Transform Resolve(string path)
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (path == root.name) return root.transform;
                if (path.StartsWith(root.name + "/", StringComparison.Ordinal))
                {
                    var found = root.transform.Find(path.Substring(root.name.Length + 1));
                    if (found != null) return found;
                }
            }
            throw new InvalidOperationException("Missing authored object: " + path);
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
        }
        static T SaveAsset<T>(T candidate, string path) where T : Object
        {
            var saved = AssetDatabase.LoadAssetAtPath<T>(path);
            if (saved == null) { AssetDatabase.CreateAsset(candidate, path); return candidate; }
            Undo.RegisterCompleteObjectUndo(saved, "Update bridge visual asset");
            EditorUtility.CopySerialized(candidate, saved); Object.DestroyImmediate(candidate); EditorUtility.SetDirty(saved); return saved;
        }
        static CampBridgePolishJournal Journal(CampRiver river)
        {
            var journal = AssetDatabase.LoadAssetAtPath<CampBridgePolishJournal>(JournalPath);
            if (journal != null)
            {
                if (journal.ScenePath != river.gameObject.scene.path || journal.RiverPath != ScenePath(river.transform))
                    throw new InvalidOperationException("The visual journal belongs to another river.");
                return journal;
            }
            EnsureFolder("Assets/Editor/CampBridgePolishData");
            journal = ScriptableObject.CreateInstance<CampBridgePolishJournal>();
            journal.ScenePath = river.gameObject.scene.path; journal.RiverPath = ScenePath(river.transform);
            journal.OriginalWaterControl = river.WaterMaterial;
            journal.FirstInvariant = Invariant(river);
            AssetDatabase.CreateAsset(journal, JournalPath);
            return journal;
        }
        static CampBridgePolishJournal.MaterialBinding RecordMaterial(CampBridgePolishJournal journal, Renderer renderer)
        {
            string path = ScenePath(renderer.transform);
            var binding = journal.Materials.Find(x => x.Path == path);
            if (binding != null) return binding;
            binding = new CampBridgePolishJournal.MaterialBinding { Path = path, Original = renderer.sharedMaterials };
            journal.Materials.Add(binding); return binding;
        }
        static void Assign(Renderer renderer, Material[] materials)
        {
            Undo.RecordObject(renderer, "Bridge visual materials"); renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); EditorUtility.SetDirty(renderer);
        }

        [MenuItem("Разлом/Лагерь/Мост и берега/Применить визуальный проход")]
        public static void ApplyMenu() => Apply();
        public static string Apply()
        {
            var river = River(); var scene = river.gameObject.scene;
            Directory.CreateDirectory(Output); EnsureFolder(Folder);
            string backup = Path.Combine(Output, "before-scene.unity");
            if (!File.Exists(backup) && !EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not back up camp.");
            string before = Invariant(river);
            var journal = Journal(river); Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
            foreach (var binding in journal.Meshes)
            {
                var current = Resolve(binding.Path).GetComponent<MeshFilter>().sharedMesh;
                if (current != binding.Original && current != binding.Polished)
                    throw new InvalidOperationException("River mesh changed since this pass was captured: " + binding.Path);
            }
            foreach (var binding in journal.Materials)
            {
                var current = Resolve(binding.Path).GetComponent<Renderer>().sharedMaterials;
                if (!current.SequenceEqual(binding.Original) && (binding.Polished == null || !current.SequenceEqual(binding.Polished)))
                    throw new InvalidOperationException("Material assignment changed since this pass was captured: " + binding.Path);
            }
            Undo.SetCurrentGroupName("Local bridge and river visual pass"); Undo.RecordObject(journal, "Bridge visual journal");
            string journalBefore=EditorJsonUtility.ToJson(journal);
            var assignmentsBefore=Object.FindAnyObjectByType<SceneWorldView>().CampRoot.GetComponentsInChildren<Renderer>(true)
                .ToDictionary(r=>r,r=>r.sharedMaterials);
            var meshesBefore=river.Geometry.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f=>f,f=>f.sharedMesh);
            var controlBefore=river.WaterMaterial;
            try
            {
            Vector3 centre = river.GetComponent<CampRiverPassage>().BridgeBounds.center;
            float waterY = river.transform.TransformPoint(new Vector3(0, river.WaterLevel, 0)).y;
            journal.Centre = centre;
            for (int kind = 0; kind < RiverMeshes.Length; kind++)
            {
                var filter = river.Geometry.Find(RiverMeshes[kind]).GetComponent<MeshFilter>();
                string path = ScenePath(filter.transform);
                var binding = journal.Meshes.Find(x => x.Path == path);
                if (binding == null)
                {
                    binding = new CampBridgePolishJournal.MeshBinding { Path = path, Original = filter.sharedMesh, OriginalHash = AssetKey(filter.sharedMesh) };
                    journal.Meshes.Add(binding);
                }
                var candidate = CampBridgePolishGeometry.Clone(binding.Original, filter.transform, centre, waterY, kind);
                binding.Polished = SaveAsset(candidate, Folder + "/River mesh " + kind + ".asset");
                Undo.RecordObject(filter, "Local visual river mesh"); filter.sharedMesh = binding.Polished;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter); EditorUtility.SetDirty(filter);
            }
            BuildStones(river, centre, waterY);
            var water = river.Geometry.Find("Лесная вода").GetComponent<Renderer>();
            var waterBinding = RecordMaterial(journal, water);
            var localWater = new Material(waterBinding.Original[0]) { name = "Bridge river — local depth" };
            localWater.shader = Shader.Find("Game/Camp Forest Water");
            localWater.SetTexture("_BridgeDepthMap", BakeDepth(river, centre, waterY));
            localWater.SetVector("_BridgeDepthBounds", new Vector4(centre.x - 8f, centre.z - 8f, 16f, 16f));
            localWater.SetFloat("_BridgePolishEnabled", 1);
            localWater.SetColor("_BridgeDeepColor", new Color(.105f,.29f,.31f,1));
            localWater.SetColor("_BridgeShallowColor", new Color(.24f,.36f,.29f,1));
            localWater.SetColor("_BridgeSkyColor", new Color(.42f,.56f,.65f,1));
            localWater.SetFloat("_BridgeSunStrength", .07f); localWater.SetFloat("_BridgeFoamStrength", .16f);
            localWater.SetFloat("_FlowSpeed", river.FlowSpeed);
            localWater = SaveAsset(localWater, Folder + "/River water.mat");
            waterBinding.Polished = new[] { localWater }; Assign(water, waterBinding.Polished);
            Undo.RecordObject(river, "Bind visible river material"); river.WaterMaterial = localWater; river.Refresh(); EditorUtility.SetDirty(river);

            var camp = Object.FindAnyObjectByType<SceneWorldView>().CampRoot.transform;
            var bridgeRenderer = river.GetComponent<CampRiverPassage>().Bridge.GetComponentInChildren<MeshRenderer>();
            // Always prepare from the saved originals, never compound a tint on subsequent runs.
            foreach (var binding in journal.Materials.Where(x => x.Path != waterBinding.Path)) Assign(Resolve(binding.Path).GetComponent<Renderer>(), binding.Original);
            foreach (var assignment in CampBridgePolishWood.Prepare(camp, bridgeRenderer, 8f))
            {
                var binding = RecordMaterial(journal, assignment.Renderer);
                binding.Polished = assignment.Materials; Assign(assignment.Renderer, assignment.Materials);
            }
            string after = Invariant(river);
            if (before != after) throw new InvalidOperationException("Visual pass changed protected navigation, bridge or painted ground. Restore from journal before saving.");
            journal.Applied = true; EditorUtility.SetDirty(journal);
            string report = Validate();
            SaveOwnedAssets(journal);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); Undo.CollapseUndoOperations(undo);
            File.WriteAllText(Path.Combine(Output, "authoring-validation.txt"), report);
            SceneView.RepaintAll(); return report;
            }
            catch
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(undo);
                foreach(var binding in assignmentsBefore)if(binding.Key!=null)binding.Key.sharedMaterials=binding.Value;
                foreach(var binding in meshesBefore)if(binding.Key!=null)binding.Key.sharedMesh=binding.Value;
                river.WaterMaterial=controlBefore;
                EditorJsonUtility.FromJsonOverwrite(journalBefore,journal);EditorUtility.SetDirty(journal);AssetDatabase.SaveAssetIfDirty(journal);
                river.Refresh(); SceneView.RepaintAll();
                throw;
            }
        }

        static void SaveOwnedAssets(CampBridgePolishJournal journal)
        {
            foreach (var binding in journal.Meshes) if (binding.Polished != null) AssetDatabase.SaveAssetIfDirty(binding.Polished);
            foreach (var binding in journal.Materials)
                if (binding.Polished != null) foreach (var material in binding.Polished)
                    if (material != null) AssetDatabase.SaveAssetIfDirty(material);
            var map = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/River depth.asset");
            if (map != null) AssetDatabase.SaveAssetIfDirty(map);
            AssetDatabase.SaveAssetIfDirty(journal);
        }

        [MenuItem("Разлом/Лагерь/Мост и берега/Вернуть исходный вид")]
        public static void RestoreMenu() => Restore();
        public static string Restore()
        {
            var river = River(); var journal = AssetDatabase.LoadAssetAtPath<CampBridgePolishJournal>(JournalPath);
            if (journal == null) return "No visual pass to restore.";
            foreach (var binding in journal.Meshes)
            {
                var filter = Resolve(binding.Path).GetComponent<MeshFilter>();
                Undo.RecordObject(filter, "Restore original river mesh"); filter.sharedMesh = binding.Original; EditorUtility.SetDirty(filter);
            }
            foreach (var binding in journal.Materials) Assign(Resolve(binding.Path).GetComponent<Renderer>(), binding.Original);
            var stones = river.transform.Find(StonesName); if (stones != null) { Undo.RecordObject(stones.gameObject, "Hide polish stones"); stones.gameObject.SetActive(false); }
            Undo.RecordObject(river, "Restore river material control"); river.WaterMaterial = journal.OriginalWaterControl; river.Refresh(); EditorUtility.SetDirty(river);
            journal.Applied = false; EditorUtility.SetDirty(journal); AssetDatabase.SaveAssetIfDirty(journal);
            EditorSceneManager.MarkSceneDirty(river.gameObject.scene); EditorSceneManager.SaveScene(river.gameObject.scene); SceneView.RepaintAll();
            return "Original mesh and material assignments restored; visual assets retained for reapplication.";
        }

        static void BuildStones(CampRiver river, Vector3 centre, float waterY)
        {
            var source = river.GetComponentsInChildren<MeshFilter>().FirstOrDefault(x => x.name.Contains("камень") && x.sharedMesh != null && !ScenePath(x.transform).Contains(StonesName));
            if (source == null) return;
            var root = river.transform.Find(StonesName);
            if (root == null)
            {
                var go = new GameObject(StonesName); Undo.RegisterCreatedObjectUndo(go, "River contact stones");
                root = go.transform; root.SetParent(river.transform, false); go.AddComponent<CampSceneryDecoration>();
            }
            Undo.RecordObject(root.gameObject,"Show river contact stones");root.gameObject.SetActive(true);
            var passage = river.GetComponent<CampRiverPassage>(); var blocked = passage.BridgeBounds; blocked.Expand(new Vector3(1.2f,0,1.0f));
            var candidates = new List<Vector3>();
            foreach (string name in new[] { RiverMeshes[0], RiverMeshes[2] })
            {
                var f = river.Geometry.Find(name).GetComponent<MeshFilter>(); var vertices = f.sharedMesh.vertices;
                int columns = name == RiverMeshes[0] ? 6 : 7, lower = name == RiverMeshes[0] ? 5 : 0;
                for (int i = lower; i < vertices.Length; i += columns)
                {
                    Vector3 p = f.transform.TransformPoint(vertices[i]); float d = Vector2.Distance(new Vector2(p.x,p.z),new Vector2(centre.x,centre.z));
                    if (d > 5.8f || d < 3.0f || (p.x>blocked.min.x && p.x<blocked.max.x && p.z>blocked.min.z && p.z<blocked.max.z)) continue;
                    candidates.Add(p);
                }
            }
            var chosen = new List<Vector3>();
            foreach (var p in candidates.OrderBy(p => Vector2.Distance(new Vector2(p.x,p.z),new Vector2(centre.x,centre.z))))
            {
                if (chosen.Any(q => Vector3.Distance(q,p)<2.1f)) continue;
                chosen.Add(p); if (chosen.Count == 3) break;
            }
            for (int i=0;i<chosen.Count;i++)
            {
                string name = "Submerged river stone " + i; var child = root.Find(name);
                if (child == null) { var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "River contact stone"); child=go.transform; child.SetParent(root,false); }
                Undo.RecordObject(child,"River contact stone position");Undo.RecordObject(child.gameObject,"River contact stone layer");
                var mf = child.GetComponent<MeshFilter>(); if(mf==null)mf=Undo.AddComponent<MeshFilter>(child.gameObject);
                Undo.RecordObject(mf,"River stone mesh");mf.sharedMesh=source.sharedMesh;
                var r = child.GetComponent<MeshRenderer>(); if(r==null)r=Undo.AddComponent<MeshRenderer>(child.gameObject);
                Undo.RecordObject(r,"River stone material");r.sharedMaterials=source.GetComponent<Renderer>().sharedMaterials;
                child.gameObject.layer=2;
                var size=source.sharedMesh.bounds.size; float s=.54f/Mathf.Max(size.x,size.z);
                child.rotation=Quaternion.Euler(0,37+i*73,0); child.localScale=new Vector3(s/root.lossyScale.x,s*.65f/root.lossyScale.y,s/root.lossyScale.z);
                Vector3 p=chosen[i]; p.y=waterY-size.y*s*.65f*.55f-source.sharedMesh.bounds.min.y*s*.65f;child.position=p;
            }
        }

        static Texture2D BakeDepth(CampRiver river, Vector3 centre, float waterY)
        {
            const int resolution=384; const float span=16f;
            var heights=new float[resolution*resolution]; for(int i=0;i<heights.Length;i++)heights[i]=float.NegativeInfinity;
            Vector2 origin=new Vector2(centre.x-8f,centre.z-8f);
            foreach(string name in RiverMeshes)
            {
                var filter=river.Geometry.Find(name).GetComponent<MeshFilter>();var vertices=filter.sharedMesh.vertices;var triangles=filter.sharedMesh.triangles;
                var world=vertices.Select(filter.transform.TransformPoint).ToArray();
                for(int i=0;i<triangles.Length;i+=3) Raster(world[triangles[i]],world[triangles[i+1]],world[triangles[i+2]],heights,resolution,origin,span);
            }
            var obstacles = river.GetComponentsInChildren<Renderer>().Where(r => (r.name.Contains("камень") || r.name.Contains("stone")) && r.bounds.min.y < waterY+.04f && r.bounds.max.y > waterY).Select(r=>r.bounds).ToList();
            var bridge=river.GetComponent<CampRiverPassage>().BridgeBounds;
            for(int side=-1;side<=1;side+=2)for(int end=-1;end<=1;end+=2)
                obstacles.Add(new Bounds(new Vector3(bridge.center.x+side*bridge.extents.x*.84f,waterY,bridge.center.z+end*bridge.extents.z*.55f),new Vector3(.20f,.30f,.20f)));
            var colors=new Color[heights.Length];
            for(int z=0;z<resolution;z++)for(int x=0;x<resolution;x++)
            {
                int i=z*resolution+x;var p=new Vector3(origin.x+(x+.5f)/resolution*span,waterY,origin.y+(z+.5f)/resolution*span);
                float depth=float.IsNegativeInfinity(heights[i])?.4f:Mathf.Max(0,waterY-heights[i]);
                float contact=0;
                foreach(var b in obstacles)
                {
                    float dx=Mathf.Max(0,Mathf.Abs(p.x-b.center.x)-b.extents.x),dz=Mathf.Max(0,Mathf.Abs(p.z-b.center.z)-b.extents.z);
                    contact=Mathf.Max(contact,1-Mathf.SmoothStep(0,1,Mathf.Sqrt(dx*dx+dz*dz)/.36f));
                }
                float shore=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.015f,.16f,depth));
                colors[i]=new Color(Mathf.Min(depth,1f),contact,shore,CampBridgePolishGeometry.SampleInfluence(p,centre));
            }
            var map=new Texture2D(resolution,resolution,TextureFormat.RGBAHalf,false,true){name="Bridge river depth (world metres)",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            map.SetPixels(colors);map.Apply(false,false);return SaveAsset(map,Folder+"/River depth.asset");
        }
        static void Raster(Vector3 a,Vector3 b,Vector3 c,float[] heights,int size,Vector2 origin,float span)
        {
            float minX=Mathf.Min(a.x,Mathf.Min(b.x,c.x)),maxX=Mathf.Max(a.x,Mathf.Max(b.x,c.x));
            float minZ=Mathf.Min(a.z,Mathf.Min(b.z,c.z)),maxZ=Mathf.Max(a.z,Mathf.Max(b.z,c.z));
            if(maxX<origin.x || minX>origin.x+span || maxZ<origin.y || minZ>origin.y+span)return;
            float denominator=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(denominator)<.0000001f)return;
            int x0=Mathf.Clamp(Mathf.FloorToInt((minX-origin.x)/span*size),0,size-1),x1=Mathf.Clamp(Mathf.CeilToInt((maxX-origin.x)/span*size),0,size-1);
            int z0=Mathf.Clamp(Mathf.FloorToInt((minZ-origin.y)/span*size),0,size-1),z1=Mathf.Clamp(Mathf.CeilToInt((maxZ-origin.y)/span*size),0,size-1);
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
            {
                float px=origin.x+(x+.5f)/size*span,pz=origin.y+(z+.5f)/size*span;
                float wa=((b.z-c.z)*(px-c.x)+(c.x-b.x)*(pz-c.z))/denominator;
                float wb=((c.z-a.z)*(px-c.x)+(a.x-c.x)*(pz-c.z))/denominator;float wc=1-wa-wb;
                if(wa>=-.0001f && wb>=-.0001f && wc>=-.0001f){int i=z*size+x;heights[i]=Mathf.Max(heights[i],wa*a.y+wb*b.y+wc*c.y);}
            }
        }

        static string AssetKey(Object asset) => asset == null ? "null" : AssetDatabase.GetAssetPath(asset)+":"+AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(asset));
        public static string Invariant(CampRiver river)
        {
            var sb=new StringBuilder();var passage=river.GetComponent<CampRiverPassage>();
            sb.Append(JsonUtility.ToJson(new ProtectedShape{Contour=river.Contour,Centres=river.BakedCentres,Normals=river.BakedNormals,Land=river.BakedLandContour,Water=river.BakedWaterContour,BakedContour=river.BakedContour,Width=river.Width,BankWidth=river.BankWidth,WaterLevel=river.WaterLevel,BakedWidth=river.BakedWidth,BakedBankWidth=river.BakedBankWidth,HasBaked=river.HasBakedShape}));
            sb.Append(JsonUtility.ToJson(passage.CrossingCentre)).Append(JsonUtility.ToJson(passage.CrossingSize));
            sb.Append(JsonUtility.ToJson(passage.FarBank)).Append(JsonUtility.ToJson(passage.BridgeBounds));
            sb.Append(AssetKey(river.FoliageBoundary));sb.Append(AssetKey(river.SourceGround));sb.Append(AssetKey(river.Ground.sharedMesh));
            foreach(var renderer in new[]{river.Ground.GetComponent<Renderer>(),river.Geometry.Find("Дальний берег").GetComponent<Renderer>()})
                if(renderer.sharedMaterial.HasProperty("_SurfaceMap"))sb.Append(AssetKey(renderer.sharedMaterial.GetTexture("_SurfaceMap")));
            foreach(var mf in passage.Bridge.GetComponentsInChildren<MeshFilter>())sb.Append(ScenePath(mf.transform)).Append(AssetKey(mf.sharedMesh)).Append(MatrixText(mf.transform.localToWorldMatrix));
            sb.Append(MatrixText(river.transform.localToWorldMatrix));
            var root=Object.FindAnyObjectByType<SceneWorldView>().CampRoot.transform;
            var sources=new List<NavMeshBuildSource>();CampNavigationGeometry.AddSources(sources,CampNavigationGeometry.Collect(root,passage.Bridge,0f),0f);river.AddNavigationSources(sources);
            foreach(var source in sources)sb.Append((int)source.shape).Append('/').Append(source.area).Append('/').Append(JsonUtility.ToJson(source.size)).Append(MatrixText(source.transform));
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-","");
        }
        static string MatrixText(Matrix4x4 matrix) { var sb=new StringBuilder();for(int i=0;i<16;i++)sb.Append(matrix[i].ToString("R",CultureInfo.InvariantCulture)).Append(',');return sb.ToString(); }
        [Serializable] sealed class ProtectedShape
        {
            public Vector2[] Contour,Centres,Normals,Land,Water,BakedContour;
            public float Width,BankWidth,WaterLevel,BakedWidth,BakedBankWidth;public bool HasBaked;
        }
        public static string Validate()
        {
            var river=River();var journal=AssetDatabase.LoadAssetAtPath<CampBridgePolishJournal>(JournalPath);var sb=new StringBuilder();
            if(journal==null)throw new InvalidOperationException("No visual journal.");
            foreach(var binding in journal.Meshes)
            {
                var filter=Resolve(binding.Path).GetComponent<MeshFilter>();var old=binding.Original.vertices;var current=filter.sharedMesh.vertices;int outside=0,changed=0;
                if (AssetKey(binding.Original) != binding.OriginalHash) throw new InvalidOperationException("Original river asset changed: " + binding.Path);
                int kind=Array.IndexOf(RiverMeshes,filter.name);int columns=kind==0?6:kind==2?7:2;
                float waterY=river.transform.TransformPoint(new Vector3(0,river.WaterLevel,0)).y;
                for(int i=0;i<old.Length;i++)
                {
                    var world=filter.transform.TransformPoint(old[i]);float influence=CampBridgePolishGeometry.SampleInfluence(world,journal.Centre);
                    bool upper=kind==0?i%columns<=1:kind==2 && i%columns>=4;
                    bool protectedVertex=influence==0 || (kind!=1 && (upper || world.y>=waterY+.12f));
                    if(protectedVertex && !old[i].Equals(current[i]))throw new InvalidOperationException("Protected river vertex changed: "+binding.Path+" index="+i);
                    if(influence==0)outside++;if(!old[i].Equals(current[i]))changed++;
                }
                var mesh=filter.sharedMesh;
                if(mesh.uv.Length!=current.Length || mesh.uv4.Length!=current.Length || mesh.colors.Length!=current.Length || mesh.normals.Length!=current.Length || mesh.tangents.Length!=current.Length)
                    throw new InvalidOperationException("Incomplete river vertex channels: "+binding.Path);
                foreach(var v in current)if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z))throw new InvalidOperationException("Non-finite river vertex.");
                var triangles=mesh.triangles;
                for(int t=0;t<triangles.Length;t+=3)
                {
                    var cross=Vector3.Cross(current[triangles[t+1]]-current[triangles[t]],current[triangles[t+2]]-current[triangles[t]]);
                    if(cross.sqrMagnitude<1e-14f || cross.y<=0)
                    {
                        // The original near-bank ribbon has a duplicated dry seam where no ground gap exists.
                        // Allow only an exactly preserved original triangle; never introduce a new degenerate face.
                        var sourceTriangles=binding.Original.triangles;
                        bool preserved=kind!=1 && t+2<sourceTriangles.Length;
                        for(int k=0;k<3 && preserved;k++)preserved=triangles[t+k]==sourceTriangles[t+k] && old[sourceTriangles[t+k]].Equals(current[triangles[t+k]]);
                        if(!preserved)throw new InvalidOperationException("New degenerate or inverted river triangle: "+binding.Path+" triangle="+(t/3));
                    }
                }
                sb.AppendLine("PASS "+filter.name+": original vertices="+old.Length+" outside preserved="+outside+" local moved="+changed+" current="+current.Length);
            }
            var visible=river.Geometry.Find("Лесная вода").GetComponent<Renderer>().sharedMaterial;
            if(visible!=river.WaterMaterial || !Mathf.Approximately(visible.GetFloat("_FlowSpeed"),river.FlowSpeed))throw new InvalidOperationException("Water material control mismatch.");
            sb.AppendLine("PASS visible water material bound; depth map="+AssetDatabase.GetAssetPath(visible.GetTexture("_BridgeDepthMap")));
            sb.AppendLine("Protected scene hash="+Invariant(river));sb.AppendLine("Original protected hash="+journal.FirstInvariant);
            sb.AppendLine("Material assignments="+journal.Materials.Count+"; applied="+journal.Applied);
            return sb.ToString();
        }
    }
}
