namespace Game.Sim
{
    /// <summary>
    /// Прибавка базы героя поверх 150 здоровья локации, 34 урона и 200 лавидия.
    ///
    /// Два значения (06.10): эталонный герой баланса 270/54 — на нём меряют мобов,
    /// с ним идут тестовые забеги и Sandbox; новый герой прогрессивного лагеря —
    /// ≈75% эталона, 200/40. Остальное до эталона добирают вещи и клятвы.
    /// Лавидий одинаков: 240 в обоих случаях, урезанный пул ломал бы ротацию навыков.
    /// </summary>
    public readonly struct HeroBaseline : System.IEquatable<HeroBaseline>
    {
        public readonly int Health, Damage, Lavidium;

        public HeroBaseline(int health, int damage, int lavidium)
        { Health = health; Damage = damage; Lavidium = lavidium; }

        /// <summary>Эталон 150+120 = 270 / 34+20 = 54. Числа — из Progression, на них баланс мобов.</summary>
        public static readonly HeroBaseline Reference = new HeroBaseline(
            Progression.HeroBaselineHealth, Progression.HeroBaselineDamage, Progression.HeroBaselineLavidium);

        /// <summary>Новая игра: 150+50 = 200 / 34+6 = 40.</summary>
        public static readonly HeroBaseline Fresh = new HeroBaseline(50, 6, Progression.HeroBaselineLavidium);

        public bool Equals(HeroBaseline o) => Health == o.Health && Damage == o.Damage && Lavidium == o.Lavidium;
        public override bool Equals(object obj) => obj is HeroBaseline o && Equals(o);
        public override int GetHashCode() => (Health * 397 ^ Damage) * 397 ^ Lavidium;
    }
}
