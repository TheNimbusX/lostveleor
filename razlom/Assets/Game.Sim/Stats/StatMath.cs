namespace Game.Sim
{
    /// <summary>
    /// Формула трёх слоёв. Живёт отдельно, потому что считает по ней не только
    /// лист статов персонажа, но и статы способностей: две копии одной формулы
    /// однажды разъедутся, и найдут это по расхождению реплея.
    /// </summary>
    public static class StatMath
    {
        /// <summary>
        /// final = (base + flat) * (1 + sumIncreased) * moreProduct
        ///
        /// moreProduct — уже посчитанное произведение множителей слоя More,
        /// а не сумма: порядок сомножителей задаёт тот, кто их накапливал,
        /// и он обязан быть каноническим.
        /// </summary>
        public static Fix64 Combine(Fix64 baseValue, Fix64 flat, Fix64 increased, Fix64 moreProduct)
            => (baseValue + flat) * (Fix64.One + increased) * moreProduct;
    }
}
