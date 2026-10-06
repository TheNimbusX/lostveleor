using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.EditorTools
{
    // Paths and persistent assets only: this editor asset does not hold scene object references.
    public sealed class CampWoodPaletteJournal : ScriptableObject
    {
        [Serializable] public sealed class Binding
        {
            public string Path;
            public Material[] Original;
            public Material[] Polished;
        }
        public string ScenePath;
        public bool Applied;
        public List<Binding> Bindings = new List<Binding>();
    }
}
