namespace Game.Sim
{
    /// <summary>
    /// Следование по маршруту из <see cref="CampWalkMap"/>.
    ///
    /// ЖИВЁТ ЗДЕСЬ, А НЕ В ПРЕДСТАВЛЕНИИ, ПО ОДНОЙ ПРИЧИНЕ: это единственный
    /// способ его проверить. Пока логика сидела в MonoBehaviour, каждая правка
    /// проверялась запуском игры и разглядыванием — две попытки подряд
    /// «исправить» походку кончились тем, что герой продолжал спотыкаться, и
    /// узнать об этом можно было только от человека за экраном.
    ///
    /// Здесь же она детерминированная и покрыта тестом, который гоняет героя
    /// вокруг настоящей стены и смотрит, дошёл ли он.
    /// </summary>
    public sealed class CampRoute
    {
        /// <summary>
        /// Радиус прибытия к конечной точке.
        ///
        /// ОБЯЗАН БЫТЬ БОЛЬШЕ ШАГА ЗА ТИК, иначе он недостижим: игрок делает
        /// 9/2 метра в секунду, то есть 0.15 м за тик, и в окно поменьше просто
        /// не попадает ни на одной границе тика — тело проскакивает точку,
        /// возвращается и топчется. Предыдущая версия ловила 0.045 м и именно
        /// так себя и вела.
        /// </summary>
        static readonly Fix64 Reach = Fix64.Ratio(1, 5);

        readonly CampWalkMap _map;
        FixVec2[] _corners = System.Array.Empty<FixVec2>();
        int _corner;

        public CampRoute(CampWalkMap map) { _map = map; }

        /// <summary>
        /// Флаги приказа для угла маршрута.
        ///
        /// Живут здесь, а не у вызывающего: выбор между «пройти насквозь» и
        /// «встать точно» — часть следования по маршруту, и разъехаться с ним
        /// он не должен.
        /// </summary>
        public static byte FlagsFor(bool final)
            => (byte)(InputFlags.MoveOrder
                      | (final ? InputFlags.NavigationWaypoint : InputFlags.NavigationTransit));

        /// <summary>Идём ли мы сейчас по маршруту.</summary>
        public bool Active { get; private set; }

        /// <summary>Углы маршрута. Нужны показу отладки и тестам.</summary>
        public int CornerCount => _corners.Length;
        public FixVec2 Corner(int index) => _corners[index];

        /// <summary>
        /// Прокладывает маршрут. Непроходимую цель разбирает сам поиск: он
        /// приводит её к ближайшей достижимой клетке, поэтому клик в скалу
        /// означает «подойди к скале», а не «стой на месте».
        /// </summary>
        public bool To(FixVec2 from, FixVec2 target)
        {
            Cancel();
            if (_map == null) return false;

            _corners = _map.FindPath(from, target);
            // Нулевой угол — то место, где герой уже стоит.
            _corner = 1;
            Active = _corners.Length > 1;
            return Active;
        }

        public void Cancel()
        {
            Active = false;
            _corners = System.Array.Empty<FixVec2>();
            _corner = 0;
        }

        /// <summary>
        /// Куда вести тело в этом тике.
        ///
        /// Возвращает false, когда маршрут закончился и телом снова
        /// распоряжается обычный ввод.
        ///
        /// <paramref name="final"/> говорит, что цель — последняя точка
        /// маршрута. Это важно снаружи: подъезд с торможением уместен только
        /// в конце, а на промежуточных углах он превращает ход в череду
        /// остановок.
        /// </summary>
        public bool Advance(FixVec2 position, out FixVec2 aim, out bool final)
        {
            aim = FixVec2.Zero;
            final = false;
            if (!Active) return false;

            // ВЕДЁМ К САМОМУ ДАЛЬНЕМУ УГЛУ, ВИДНОМУ ПО ПРЯМОЙ.
            //
            // Идти строго от угла к углу нельзя: угол считается пройденным
            // только вблизи, а тело двигается дискретными шагами и в это окно
            // не попадает. Пересчёт каждый тик заодно распрямляет путь — пока
            // цель видна напрямую, промежуточные углы не нужны, и вместо
            // лесенки, которую даёт поиск по клеткам, выходит прямая.
            for (int i = _corners.Length - 1; i > _corner; i--)
                if (_map.CanTravel(position, _corners[i])) { _corner = i; break; }

            final = _corner == _corners.Length - 1;
            if (final && FixVec2.DistanceSq(position, _corners[_corner]) < Reach * Reach)
            {
                Cancel();
                return false;
            }

            aim = _corners[_corner];
            return true;
        }
    }
}
