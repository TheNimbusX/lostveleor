namespace Game.Sim
{
    /// <summary>
    /// Формы навыков в бою и билд ветки сабли (план форм 02.10).
    ///
    /// ФОРМА — УЗЕЛ СБОРКИ, А НЕ НОВОЕ ОПРЕДЕЛЕНИЕ: AbilityBuild.Form ставит узел
    /// NodeKind.Form, DefinitionId прежний. Сборка слота может смениться посреди
    /// боя (мини-меню над навыком с элиты), поэтому код формы каждый тик
    /// спрашивает FormIs(slot, …) и не держит форму в своём состоянии.
    ///
    /// САБЛЯ (ЛКМ) — ПСЕВДОЛИНИЯ PelagKit.SabreLine: свой билд, не шестой слот.
    /// Пока у сабли нет ни узлов, ни форм, билд не активен, серия читает свои
    /// константы бит в бит и хеш не меняется.
    /// </summary>
    public sealed partial class Simulation
    {
        private readonly AbilityBuild _sabreBuild = new AbilityBuild();
        private bool _sabreBuildActive;

        /// <summary>Билд серии сабли или null, пока у сабли нет ни талантов, ни формы.</summary>
        public AbilityBuild BasicAttackBuild => _sabreBuildActive ? _sabreBuild : null;

        /// <summary>
        /// Ставит узлы ветки сабли (её таланты и форму) — зовёт RunLoadout.ApplyTo.
        /// Ноль узлов — билд снят, серия на своих константах. Узлы сортирует Rebuild.
        /// </summary>
        public void SetBasicAttack(AbilityNode[] nodes, int count)
        {
            if (nodes == null || count <= 0)
            {
                _sabreBuildActive = false;
                return;
            }
            _sabreBuild.Rebuild(AbilityDefinition.SabreCombo(), nodes, count);
            _sabreBuildActive = true;
        }

        /// <summary>Форма способности в слоте; пустой слот или без формы — None.</summary>
        public PelagForm FormAt(int slot)
        {
            AbilityBuild build = (uint)slot < (uint)AbilitySlots ? _abilityBuilds[slot] : null;
            return build != null ? build.Form : PelagForm.None;
        }

        /// <summary>В слоте сейчас способность именно этой формы.</summary>
        public bool FormIs(int slot, PelagForm form) => form != PelagForm.None && FormAt(slot) == form;

        /// <summary>Дальность сектора серии: из билда сабли, без него — SabreReach.</summary>
        private Fix64 SabreReachNow => _sabreBuildActive ? _sabreBuild.Get(AbilityStatType.Radius) : SabreReach;

        /// <summary>Косинус полусектора серии: из билда сабли, без него — SabreArcCos.</summary>
        private Fix64 SabreArcCosNow => _sabreBuildActive ? _sabreBuild.Get(AbilityStatType.ArcCosine) : SabreArcCos;

        /// <summary>Хеш билда сабли — только активного; цепочкой из HashSabreCombo.</summary>
        private void HashSabreBuild(ref ulong hash)
        {
            if (!_sabreBuildActive) return;
            Hashing.Mix(ref hash, 0x53425544);   // "SBUD"
            _sabreBuild.HashInto(ref hash);
        }
    }
}
