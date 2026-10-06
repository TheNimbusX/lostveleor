using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.EditorTools
{
    // Editor-only journal: stores asset references and scene paths, never serializes scene objects into an asset.
    public sealed class CampBridgePolishJournal : ScriptableObject
    {
        [Serializable] public sealed class MeshBinding { public string Path; public Mesh Original; public Mesh Polished; public string OriginalHash; }
        [Serializable] public sealed class MaterialBinding { public string Path; public Material[] Original; public Material[] Polished; }
        public string ScenePath;
        public string RiverPath;
        public Material OriginalWaterControl;
        public List<MeshBinding> Meshes = new List<MeshBinding>();
        public List<MaterialBinding> Materials = new List<MaterialBinding>();
        public Vector3 Centre;
        public bool Applied;
        public string FirstInvariant;
    }
}
