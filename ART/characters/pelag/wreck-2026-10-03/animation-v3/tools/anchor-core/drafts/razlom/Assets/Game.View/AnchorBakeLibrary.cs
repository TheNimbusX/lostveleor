using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Запечки якоря из Resources/Weapons/Pelag/AnchorBakes (*.anchorbake.json, DESIGN §2.1). Грузятся один раз
    /// на героя; битый файл — предупреждение и пропуск (голова тогда живая, а не в позе формулами).
    /// </summary>
    public sealed class AnchorBakeLibrary
    {
        public const string Folder = "Weapons/Pelag/AnchorBakes";
        private const string Suffix = ".anchorbake";
        private readonly Dictionary<string, AnchorBake> _byName = new Dictionary<string, AnchorBake>();
        private readonly Dictionary<string, List<AnchorBake>> _byClip = new Dictionary<string, List<AnchorBake>>();

        public int Count => _byName.Count;

        public static AnchorBakeLibrary Load()
        {
            var library = new AnchorBakeLibrary();
            foreach (var asset in Resources.LoadAll<TextAsset>(Folder))
            {
                if (asset == null || !asset.name.EndsWith(Suffix)) continue;
                string name = asset.name.Substring(0, asset.name.Length - Suffix.Length);
                AnchorBakeData data = null;
                try { data = JsonUtility.FromJson<AnchorBakeData>(asset.text); }
                catch (System.Exception e) { Debug.LogWarning($"[anchor-rig] запечка {name}: {e.Message}"); continue; }
                var bake = new AnchorBake(data);
                if (!bake.Valid) { Debug.LogWarning($"[anchor-rig] запечка {name} отклонена: {bake.Error}"); continue; }
                library.Add(name, bake);
            }
            return library;
        }

        public void Add(string name, AnchorBake bake)
        {
            _byName[name] = bake;
            string clip = string.IsNullOrEmpty(bake.Clip) ? name : bake.Clip;
            if (!_byClip.TryGetValue(clip, out var list)) _byClip[clip] = list = new List<AnchorBake>();
            list.Add(bake);
        }

        public bool HasClip(string clip) => _byClip.ContainsKey(clip);

        public bool HasAll(string[] clips)
        {
            foreach (var clip in clips) if (!HasClip(clip)) return false;
            return true;
        }

        /// <summary>
        /// Вариант замаха (_w&lt;N&gt;) или выхода (_p&lt;K&gt;); нет точного — ближайший по тикам замаха того же клипа
        /// (<paramref name="exact"/> = false: путь растягивается часами Sim, это пишется в лог).
        /// </summary>
        public AnchorBake Find(string clip, int windupTicks, int variant, out bool exact)
        {
            exact = true;
            if (string.IsNullOrEmpty(clip)) { exact = false; return null; }
            if (_byName.TryGetValue(AnchorRigWreckPlan.BakeName(clip, windupTicks, variant), out var hit)) return hit;
            if (_byName.TryGetValue(clip, out hit)) { exact = windupTicks <= 0 && variant < 0; return hit; }
            exact = false;
            if (!_byClip.TryGetValue(clip, out var list) || list.Count == 0) return null;
            AnchorBake best = list[0];
            foreach (var bake in list)
                if (Mathf.Abs(bake.WindupTicks - windupTicks) < Mathf.Abs(best.WindupTicks - windupTicks)) best = bake;
            return best;
        }
    }
}
