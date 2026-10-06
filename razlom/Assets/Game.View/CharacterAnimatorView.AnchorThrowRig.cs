namespace Game.View
{
    // Мост тела Броска якоря к ригу якоря на цепи (artifacts/anchor-core). Кладётся ТОЛЬКО вместе с ригом
    // (apply.py вида анимации Броска: если в проекте есть PelagAnchorRig.Throw.cs); без него partial-метод пустой.
    public sealed partial class CharacterAnimatorView
    {
        /// <summary>Клип ловли дошёл до кадра «якорь на спину» (Catch 3): риг кончает маятник ловли и убирает голову.</summary>
        partial void AnchorThrowRigStowCue(int serial)
        {
            PelagAnchorRig rig = GetComponent<PelagAnchorRig>();
            if (rig == null) rig = GetComponentInChildren<PelagAnchorRig>(true);
            if (rig != null) rig.CueThrowStow(serial);
        }
    }
}
