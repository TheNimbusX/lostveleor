using System;

namespace Game.Sim
{
    /// <summary>Фигура метки на земле. Значение входит в хеш — новые только в конец.</summary>
    public enum TelegraphShape : byte
    {
        /// <summary>Круг радиуса Radius вокруг Origin. Прыжок, плод.</summary>
        Circle = 0,

        /// <summary>Сектор от Origin вдоль Direction: Radius, половина раствора ArcCos, дыра InnerRadius.</summary>
        Sector = 1,

        /// <summary>Полоса от Origin вдоль Direction длиной Length и шириной Width. Таран.</summary>
        Lane = 2,

        /// <summary>Кольцо между InnerRadius и Radius вокруг Origin. Вой.</summary>
        Ring = 3,
    }

    public enum TelegraphState : byte
    {
        None = 0,

        /// <summary>Заполняется: удар ещё впереди.</summary>
        Active = 1,

        /// <summary>Удар случился. Метка доживает TelegraphLingerTicks для вспышки.</summary>
        Resolved = 2,

        /// <summary>Удара не будет. Метка доживает TelegraphLingerTicks для угасания.</summary>
        Cancelled = 3,
    }

    [Flags]
    public enum TelegraphFlags : byte
    {
        None = 0,

        /// <summary>
        /// Метку рисует общий GroundTelegraphView. Без флага метка всё равно
        /// живёт в симуляции — для жетонов, проверок и отладки, — но на земле
        /// её рисует собственный вид моба. Так старые виды Вендиго и
        /// Камнекопыта доживают до приёмки общего, не рисуясь дважды.
        /// </summary>
        SharedView = 1,
    }

    /// <summary>
    /// Одна метка удара на земле.
    ///
    /// ФИГУРА ОДНА НА ОТРИСОВКУ И НА УРОН. Вид берёт поля отсюда, владелец
    /// удара проверяет попадание через Simulation.TelegraphContains по тем
    /// же полям. Отдельной «логической» фигуры нет, поэтому нарисованное и
    /// настоящее разъехаться не могут — ровно та честность, без которой
    /// уклонение превращается в угадывание.
    /// </summary>
    public readonly struct EnemyTelegraph
    {
        public readonly int Serial, Source, StartTick, ImpactTick, EndTick;
        public readonly TelegraphShape Shape;
        public readonly TelegraphState State;
        public readonly TelegraphFlags Flags;
        public readonly FixVec2 Origin, Direction;
        public readonly Fix64 Radius, InnerRadius, ArcCos, Width, Length;

        public EnemyTelegraph(int serial, int source, int startTick, int impactTick, int endTick,
            TelegraphShape shape, TelegraphState state, TelegraphFlags flags, FixVec2 origin,
            FixVec2 direction, Fix64 radius, Fix64 innerRadius, Fix64 arcCos, Fix64 width, Fix64 length)
        {
            Serial = serial; Source = source; StartTick = startTick; ImpactTick = impactTick; EndTick = endTick;
            Shape = shape; State = state; Flags = flags; Origin = origin; Direction = direction;
            Radius = radius; InnerRadius = innerRadius; ArcCos = arcCos; Width = width; Length = length;
        }

        public bool IsActive => State == TelegraphState.Active;
        public bool SharedView => (Flags & TelegraphFlags.SharedView) != 0;

        // Заготовки фигур без времени и номера: номер и тики ставит OpenTelegraph.
        public static EnemyTelegraph Circle(FixVec2 center, Fix64 radius)
            => new EnemyTelegraph(0, -1, 0, 0, 0, TelegraphShape.Circle, TelegraphState.None,
                TelegraphFlags.None, center, new FixVec2(Fix64.One, Fix64.Zero), radius,
                Fix64.Zero, Fix64.Zero, Fix64.Zero, Fix64.Zero);

        public static EnemyTelegraph Sector(FixVec2 origin, FixVec2 direction, Fix64 radius, Fix64 arcCos,
            Fix64 innerRadius = default)
            => new EnemyTelegraph(0, -1, 0, 0, 0, TelegraphShape.Sector, TelegraphState.None,
                TelegraphFlags.None, origin, direction, radius, innerRadius, arcCos, Fix64.Zero, Fix64.Zero);

        public static EnemyTelegraph Lane(FixVec2 origin, FixVec2 direction, Fix64 length, Fix64 width)
            => new EnemyTelegraph(0, -1, 0, 0, 0, TelegraphShape.Lane, TelegraphState.None,
                TelegraphFlags.None, origin, direction, Fix64.Zero, Fix64.Zero, Fix64.Zero, width, length);

        public static EnemyTelegraph Ring(FixVec2 center, Fix64 innerRadius, Fix64 outerRadius)
            => new EnemyTelegraph(0, -1, 0, 0, 0, TelegraphShape.Ring, TelegraphState.None,
                TelegraphFlags.None, center, new FixVec2(Fix64.One, Fix64.Zero), outerRadius,
                innerRadius, Fix64.Zero, Fix64.Zero, Fix64.Zero);

        internal EnemyTelegraph Stamp(int serial, int source, int start, int impact, int end, TelegraphFlags flags)
            => new EnemyTelegraph(serial, source, start, impact, end, Shape, TelegraphState.Active, flags,
                Origin, Direction, Radius, InnerRadius, ArcCos, Width, Length);

        internal EnemyTelegraph Finish(TelegraphState state, int endTick)
            => new EnemyTelegraph(Serial, Source, StartTick, ImpactTick, endTick, Shape, state, Flags,
                Origin, Direction, Radius, InnerRadius, ArcCos, Width, Length);
    }

    /// <summary>
    /// ОБЩИЙ СПИСОК МЕТОК НА ЗЕМЛЕ.
    ///
    /// Раньше каждый моб держал свою метку в своём состоянии, и каждый вид
    /// рисовал её по-своему. Список один на всех: вид получает единый стиль и
    /// единое заполнение по тикам, а жетоны атак и проверки — одно место, где
    /// видно, сколько угроз висит над игроком прямо сейчас.
    ///
    /// Массив фиксированный и выделен в конструкторе, как пул плодов: метки
    /// открываются в бою, и аллокации там запрещены. Занятый слот — ненулевой
    /// серийный номер; свободный ищется снизу вверх, поэтому раскладка по
    /// слотам зависит только от порядка открытия и одинакова на всех машинах.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Сколько тиков метка остаётся на земле после удара или отмены. Вид
        /// успевает показать вспышку или угасание; урона в это время нет.
        /// Короче паузы между ударами любого моба — иначе у одного владельца
        /// жило бы больше двух меток, и места в пуле могло бы не хватить.
        /// </summary>
        public const int TelegraphLingerTicks = 6;

        // Два места на сущность: действующая метка и доживающая прошлая.
        private const int TelegraphSlotsPerEntity = 2;
        private readonly EnemyTelegraph[] _telegraphs;
        private int _telegraphSerial, _telegraphHighWater;

        public int TelegraphCapacity => _telegraphs.Length;

        /// <summary>Выше этого слота меток нет. Виду не нужно обходить весь пул.</summary>
        public int TelegraphHighWater => _telegraphHighWater;

        public bool TryGetTelegraph(int slot, out EnemyTelegraph telegraph)
        {
            telegraph = (uint)slot < (uint)_telegraphs.Length ? _telegraphs[slot] : default;
            return telegraph.Serial != 0;
        }

        /// <summary>
        /// Открывает метку от текущего тика. Возвращает слот или -1, если пул
        /// полон; владелец удара от этого не ломается — попадание он может
        /// проверить по той же заготовке фигуры.
        /// </summary>
        public int OpenTelegraph(int source, in EnemyTelegraph shape, int impactTick, int endTick,
            TelegraphFlags flags)
        {
            int slot = 0;
            while (slot < _telegraphs.Length && _telegraphs[slot].Serial != 0) slot++;
            if (slot == _telegraphs.Length) return -1;

            var telegraph = shape.Stamp(++_telegraphSerial, source, Tick, impactTick,
                Math.Max(endTick, impactTick), flags);
            _telegraphs[slot] = telegraph;
            if (slot >= _telegraphHighWater) _telegraphHighWater = slot + 1;
            _events.Add(new SimEvent(SimEventType.TelegraphOpened, source, -1, slot,
                telegraph.SharedView, telegraph.Origin, actionVariant: telegraph.Serial));
            return slot;
        }

        /// <summary>
        /// Снимает все ещё не сработавшие метки владельца. Уже сработавшие
        /// доживают свою вспышку: отмена после удара — не отмена удара.
        /// </summary>
        public void CancelTelegraphsOf(int source)
        {
            for (int slot = 0; slot < _telegraphHighWater; slot++)
            {
                var telegraph = _telegraphs[slot];
                if (telegraph.Serial == 0 || telegraph.Source != source || !telegraph.IsActive) continue;
                _telegraphs[slot] = telegraph.Finish(TelegraphState.Cancelled, Tick + TelegraphLingerTicks);
                _events.Add(new SimEvent(SimEventType.TelegraphCancelled, source, -1, slot,
                    telegraph.SharedView, telegraph.Origin, actionVariant: telegraph.Serial));
            }
        }

        /// <summary>Удар по метке случился. Проверку попадания делает владелец.</summary>
        private void ResolveTelegraph(int slot, int serial)
        {
            if ((uint)slot >= (uint)_telegraphs.Length) return;
            var telegraph = _telegraphs[slot];
            if (telegraph.Serial != serial || !telegraph.IsActive) return;
            _telegraphs[slot] = telegraph.Finish(TelegraphState.Resolved, Tick + TelegraphLingerTicks);
        }

        /// <summary>
        /// Слот метки по её серийному номеру или -1, если метки уже нет в пуле.
        /// Для владельцев с несколькими метками разом (линия шипов): номер
        /// переживает перестановки, слот хранить не нужно.
        /// </summary>
        public int FindTelegraph(int serial)
        {
            if (serial <= 0) return -1;
            for (int slot = 0; slot < _telegraphHighWater; slot++)
                if (_telegraphs[slot].Serial == serial) return slot;
            return -1;
        }

        /// <summary>
        /// Удар по одной метке, известной по номеру. Возвращает true, если
        /// метка была действующей и теперь сработала; снятая, уже сработавшая
        /// или истёкшая — false, и удара по ней быть не должно. Прочие метки
        /// того же владельца не трогает.
        /// </summary>
        private bool ResolveTelegraphSerial(int serial)
        {
            int slot = FindTelegraph(serial);
            if (slot < 0 || !_telegraphs[slot].IsActive) return false;
            ResolveTelegraph(slot, serial);
            return true;
        }

        /// <summary>Снимает одну ещё не сработавшую метку по номеру. Событие — как у CancelTelegraphsOf.</summary>
        private bool CancelTelegraphSerial(int serial)
        {
            int slot = FindTelegraph(serial);
            if (slot < 0 || !_telegraphs[slot].IsActive) return false;
            var telegraph = _telegraphs[slot];
            _telegraphs[slot] = telegraph.Finish(TelegraphState.Cancelled, Tick + TelegraphLingerTicks);
            _events.Add(new SimEvent(SimEventType.TelegraphCancelled, telegraph.Source, -1, slot,
                telegraph.SharedView, telegraph.Origin, actionVariant: telegraph.Serial));
            return true;
        }

        /// <summary>То же для владельцев, которые номер слота не хранят: Вендиго, Камнекопыт.</summary>
        private void ResolveTelegraphsOf(int source)
        {
            for (int slot = 0; slot < _telegraphHighWater; slot++)
            {
                var telegraph = _telegraphs[slot];
                if (telegraph.Serial == 0 || telegraph.Source != source || !telegraph.IsActive) continue;
                _telegraphs[slot] = telegraph.Finish(TelegraphState.Resolved, Tick + TelegraphLingerTicks);
            }
        }

        /// <summary>
        /// Освобождает доживших метки. Первой стадией тика: метка с EndTick,
        /// равным текущему тику, этот тик уже не видна.
        /// </summary>
        private void ExpireTelegraphs()
        {
            for (int slot = 0; slot < _telegraphHighWater; slot++)
                if (_telegraphs[slot].Serial != 0 && Tick >= _telegraphs[slot].EndTick)
                    _telegraphs[slot] = default;
            while (_telegraphHighWater > 0 && _telegraphs[_telegraphHighWater - 1].Serial == 0)
                _telegraphHighWater--;
        }

        private void ResetTelegraphs()
        {
            Array.Clear(_telegraphs, 0, _telegraphs.Length);
            _telegraphSerial = _telegraphHighWater = 0;
        }

        /// <summary>
        /// Задевает ли тело радиуса bodyRadius фигуру метки. Тело попадает,
        /// если пересекает фигуру хотя бы краем: игрок видит на земле фигуру,
        /// а не точку в центре своего персонажа, и выходить из неё обязан
        /// целиком.
        ///
        /// Сектор считается честно, вместе с боковыми кромками: одна проверка
        /// «центр в угле» пропускала бы тело, стоящее у самого края, хотя
        /// плечом оно уже внутри нарисованного.
        /// </summary>
        public static bool TelegraphContains(in EnemyTelegraph t, FixVec2 point, Fix64 bodyRadius)
        {
            FixVec2 offset = point - t.Origin;
            Fix64 distanceSq = offset.LengthSq;
            Fix64 bodySq = bodyRadius * bodyRadius;
            switch (t.Shape)
            {
                case TelegraphShape.Circle:
                {
                    Fix64 reach = t.Radius + bodyRadius;
                    return distanceSq <= reach * reach;
                }
                case TelegraphShape.Ring:
                {
                    Fix64 reach = t.Radius + bodyRadius;
                    if (distanceSq > reach * reach) return false;
                    Fix64 hole = t.InnerRadius - bodyRadius;
                    return hole.Raw <= 0 || distanceSq >= hole * hole;
                }
                case TelegraphShape.Sector:
                {
                    Fix64 reach = t.Radius + bodyRadius;
                    if (distanceSq > reach * reach) return false;
                    Fix64 hole = t.InnerRadius - bodyRadius;
                    if (hole.Raw > 0 && distanceSq < hole * hole) return false;
                    if (FixVec2.WithinArc(t.Direction, offset, t.ArcCos)) return true;

                    // Центр вне угла — тело всё ещё может лежать на кромке.
                    // Кромки получаются поворотом взгляда на половину раствора.
                    Fix64 cos = t.ArcCos;
                    Fix64 sin = Fix64.Sqrt(Fix64.Max(Fix64.Zero, Fix64.One - cos * cos));
                    FixVec2 d = t.Direction;
                    var left = new FixVec2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
                    var right = new FixVec2(d.X * cos + d.Y * sin, d.Y * cos - d.X * sin);
                    return EdgeDistanceSq(offset, left, t.InnerRadius, t.Radius) <= bodySq
                        || EdgeDistanceSq(offset, right, t.InnerRadius, t.Radius) <= bodySq;
                }
                case TelegraphShape.Lane:
                {
                    // Прямоугольник в осях полосы: ближайшая точка — зажатые координаты.
                    Fix64 along = FixVec2.Dot(offset, t.Direction);
                    Fix64 across = Fix64.Abs(t.Direction.X * offset.Y - t.Direction.Y * offset.X);
                    Fix64 half = t.Width / 2;
                    Fix64 outAlong = along.Raw < 0 ? -along : along > t.Length ? along - t.Length : Fix64.Zero;
                    Fix64 outAcross = across > half ? across - half : Fix64.Zero;
                    return outAlong * outAlong + outAcross * outAcross <= bodySq;
                }
            }
            return false;
        }

        private static Fix64 EdgeDistanceSq(FixVec2 offset, FixVec2 edge, Fix64 from, Fix64 to)
        {
            Fix64 along = Fix64.Clamp(FixVec2.Dot(offset, edge), from, to);
            return (offset - edge * along).LengthSq;
        }

        private void HashTelegraphs(ref ulong hash)
        {
            if (_telegraphSerial == 0) return;
            Hashing.Mix(ref hash, 0x54454C45); Hashing.Mix(ref hash, _telegraphSerial);
            Hashing.Mix(ref hash, _telegraphHighWater);
            for (int slot = 0; slot < _telegraphHighWater; slot++)
            {
                var t = _telegraphs[slot];
                if (t.Serial == 0) continue;
                Hashing.Mix(ref hash, slot); Hashing.Mix(ref hash, t.Serial); Hashing.Mix(ref hash, t.Source);
                Hashing.Mix(ref hash, t.StartTick); Hashing.Mix(ref hash, t.ImpactTick); Hashing.Mix(ref hash, t.EndTick);
                Hashing.Mix(ref hash, (int)t.Shape); Hashing.Mix(ref hash, (int)t.State); Hashing.Mix(ref hash, (int)t.Flags);
                Hashing.Mix(ref hash, t.Origin.X); Hashing.Mix(ref hash, t.Origin.Y);
                Hashing.Mix(ref hash, t.Direction.X); Hashing.Mix(ref hash, t.Direction.Y);
                Hashing.Mix(ref hash, t.Radius); Hashing.Mix(ref hash, t.InnerRadius); Hashing.Mix(ref hash, t.ArcCos);
                Hashing.Mix(ref hash, t.Width); Hashing.Mix(ref hash, t.Length);
            }
        }
    }
}
