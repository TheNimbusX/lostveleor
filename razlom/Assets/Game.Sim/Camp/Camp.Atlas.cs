namespace Game.Sim
{
    /// <summary>
    /// Атлас (06.10) — только артефакты: восемь штук набора акта I, «Открыто N из 8».
    /// Артефакт открывается, когда его взяли в реальном забеге (с босса или из тайника);
    /// отказ от награды и тестовый забег не открывают. Маска и OpenArtifact живут в
    /// Camp.RunLink рядом с остальным, что забег отдаёт лагерю. Основ в атласе больше нет.
    /// </summary>
    public sealed partial class Camp
    {
        /// <summary>Сколько артефактов открыто в атласе.</summary>
        public int OpenedArtifactCount
        {
            get { int n = 0; for (uint m = _openedArtifacts; m != 0; m &= m - 1) n++; return n; }
        }
    }
}
