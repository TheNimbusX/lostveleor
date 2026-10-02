using System;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// «Тлеющие метки» у края экрана (владелец 30.09, выбор 2a на доске concepts-2026-09-30-hud-polish):
    /// то, что важно и сейчас за кадром, получает круглую метку на краю — знак врага тушью, шеврон наружу
    /// к цели и при первом появлении подпись «Вендиго · 14 м», которая гаснет до одного знака.
    ///
    /// Что метится: элита (и босс), стрелки (Шипомёт, Плюй-плод) и атаки, начатые за кадром, — метка
    /// удара на земле (общий список меток Sim), разбег Камнекопыта, линия шипов Шипомёта; угроза красная
    /// и пульсирует, пока удар не случился или не снят. После зачистки — спокойные золотые метки к
    /// выходу и к тайникам, пока их не забрали.
    ///
    /// Пределы: не больше <see cref="MaxMarks"/> меток; соседи на краю сливаются в одну с «×N»; метки
    /// обходят панели HUD (портрет со строкой эффектов, способности, зелья, миникарта, плашка забега,
    /// строка добычи, полоса босса); цель вошла в кадр — метка уходит; вдоль края метка скользит.
    /// Раскладка — чистая математика <see cref="WorldEdgeMarksLayout"/> (тесты вне Unity); здесь —
    /// только данные Sim, камера и показ. Кадровых аллокаций нет: пулы и массивы — заранее.
    ///
    /// Префаб Resources/UI/Prefabs/WorldEdgeMarksWc (сборщик WorldEdgeMarksBuilder, дальше правится
    /// руками); ставит его <see cref="RunWorldView"/>. Порядок 2060 — после камеры, тряски и меток мира.
    /// </summary>
    [DefaultExecutionOrder(2060)]
    public sealed class WorldEdgeMarks : MonoBehaviour
    {
        public const string PrefabResource = "UI/Prefabs/WorldEdgeMarksWc";
        const int TagExit = -1, TagCache = -2;
        const int MaxCandidates = 48, MaxBlocked = 16, MaxPlaces = 8;

        [Tooltip("Версия раскладки префаба (WorldEdgeMarksBuilder.LayoutVersion): ставят сборщик и миграции")]
        public int LayoutVersion;

        [Header("Метка")]
        [Tooltip("Образец метки: лежит выключенным, метки — его копии")] public WorldEdgeMarksItem Template;
        [Tooltip("Знаки врагов по номеру EnemyKind (1 — хранитель, 4 — вендиго…); пусто — знак [0]")]
        public Texture[] KindIcons = new Texture[10];
        public Texture ExitIcon, CacheIcon;

        [Header("Раскладка, единицы холста 1920×1080")]
        [Tooltip("Центр метки от края экрана")] public float EdgeInset = 62f;
        [Tooltip("Диаметр метки: по нему метки обходят панели HUD и сливаются")] public float MarkSize = 46f;
        [Tooltip("Соседние метки ближе этого зазора сливаются в одну с «×N»")] public float MergeGap = 10f;
        [Tooltip("Цель ближе к краю экрана, чем это, ещё за кадром (видна половина тела)")] public float ScreenMargin = 40f;
        [Tooltip("Показанная метка уходит, когда цель вошла в кадр ещё на столько")] public float Hysteresis = 36f;
        [Tooltip("Поле вокруг панелей HUD")] public float HudPadding = 10f;
        [Tooltip("Над портретом — строка эффектов (корни, зелья…): метка туда не встаёт")] public float PortraitRowReserve = 72f;
        [Tooltip("Запас по бокам нижней полосы HUD (портрет слева, зелья справа): метка у края не встаёт вплотную " +
                 "к ним и не читается ещё одной ячейкой полосы (проверка 30.09: «Выход» вставал седьмым слотом за зельями)")]
        public float BottomBarSideReserve = 60f;
        [Range(1, MaxPlaces)] public int MaxMarks = 5;
        [Tooltip("Скольжение метки вдоль края, 1/с")] public float GlideRate = 9f;
        [Tooltip("Точка цели над землёй, м: середина тела")] public float TargetHeight = 1f;

        [Header("Пульс угрозы («Вспышки и мерцание: Мягче» его ослабляет)")]
        [Tooltip("Скорость пульса, рад/с")] public float PulseSpeed = 6f;
        [Range(0f, 1f)] public float PulseGlow = .7f;
        [Range(0f, .3f)] public float PulseScale = .07f;

        sealed class Slot
        {
            public WorldEdgeMarksItem Item;
            public int Key = int.MinValue, Tag = int.MinValue, Meters = -1;
            public bool Used, Labelled;
            public float S, ShownAt;
            public string Text;
        }

        TickDriver _driver;
        Canvas _canvas;
        Slot[] _slots = new Slot[0];
        readonly EdgeMarkCandidate[] _candidates = new EdgeMarkCandidate[MaxCandidates];
        readonly EdgeMarkCandidate[] _marks = new EdgeMarkCandidate[MaxPlaces];
        readonly EdgeRect[] _blocked = new EdgeRect[MaxBlocked];
        readonly float[] _blockFrom = new float[MaxBlocked * 4], _blockTo = new float[MaxBlocked * 4];
        readonly Vector3[] _corners = new Vector3[4];

        // Состояние целей: «была за кадром» (гистерезис), «подпись уже показана», угроза этого кадра.
        bool[] _off = new bool[0], _labelled = new bool[0], _threat = new bool[0];
        readonly bool[] _exitOff = new bool[MaxPlaces], _exitLabelled = new bool[MaxPlaces];
        readonly bool[] _cacheOff = new bool[MaxPlaces], _cacheLabelled = new bool[MaxPlaces];

        Simulation _shownSim;
        int _generation = -1, _depth = -1;
        float _lastNow = -1f, _nextFind;
        CombatHudView _combat;
        RunHudView _runView;
        Camera _camera;

        // Кадр: экран, вставка, герой и оси камеры на земле.
        EdgeRect _screen, _inset;
        float _perimeter, _heroX, _heroY, _scale;
        int _intervals;
        Vector3 _heroWorld, _right, _forward;

        /// <summary>Поставить метки рядом с метками мира забега. Нет префаба — меток нет, игра не ломается.</summary>
        public static WorldEdgeMarks Attach(Transform parent, TickDriver driver)
        {
            var prefab = Resources.Load<GameObject>(PrefabResource);
            if (prefab == null) return null;
            GameObject instance = Instantiate(prefab, parent);
            UiScaleFollower.Attach(instance);
            var marks = instance.GetComponentInChildren<WorldEdgeMarks>(true);
            if (marks != null) marks.Initialize(driver);
            return marks;
        }

        public void Initialize(TickDriver driver)
        {
            _driver = driver;
            _canvas = GetComponent<Canvas>();
            if (Template == null) return;
            Template.gameObject.SetActive(false);
            // Пул сразу на весь предел: копии не рождаются посреди боя.
            _slots = new Slot[MaxPlaces];
            for (int i = 0; i < _slots.Length; i++)
            {
                WorldEdgeMarksItem item = Instantiate(Template, Template.transform.parent);
                item.name = "Метка " + (i + 1);
                item.gameObject.SetActive(false);
                _slots[i] = new Slot { Item = item };
            }
        }

        void LateUpdate()
        {
            float now = UiMotion.Now;
            float dt = _lastNow < 0f ? 0f : Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;
            RiftRun run = _driver != null ? _driver.Run : null;
            Simulation sim = run != null ? run.Sim : null;
            ResetOnNewArena(sim, run);
            if (!Live(run) || !Frame(sim)) { LeaveAll(); return; }

            int count = Collect(run, sim);
            float merge = MarkSize * _scale + MergeGap * _scale;
            int marks = WorldEdgeMarksLayout.Merge(_candidates, count, _perimeter, merge, MaxMarks, _marks);
            Show(marks, now, dt, run);
        }

        // ---------------------------------------------------------------- когда показывать

        bool Live(RiftRun run)
        {
            if (_driver == null || _slots.Length == 0 || _driver.GameplayPaused || CaptureRig.NoBars) return false;
            GameSession session = _driver.Session;
            if (session == null || session.Mode != GameMode.Rift || run == null) return false;
            if (run.Phase != RunPhase.Clearing && run.Phase != RunPhase.SeekingExit) return false;
            FindPanels();
            // Боевой HUD спрятан (затухание под окном, итоги) — и метки вместе с ним.
            return _combat == null || _combat.gameObject.activeInHierarchy;
        }

        /// <summary>Камера, экран, вставка, панели и герой этого кадра; false — показывать не на чем.</summary>
        bool Frame(Simulation sim)
        {
            if (sim == null || sim.Entities.Count <= Simulation.PlayerId) return false;
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (_camera == null) return false;
            _scale = _canvas != null ? Mathf.Max(.01f, _canvas.scaleFactor) : 1f;
            Rect safe = Screen.safeArea;
            _screen = new EdgeRect(safe.xMin, safe.yMin, safe.xMax, safe.yMax);
            _inset = _screen.Inset(EdgeInset * _scale);
            if (_inset.IsEmpty) return false;
            _perimeter = WorldEdgeMarksLayout.Perimeter(_inset);
            int panels = CollectPanels();
            _intervals = WorldEdgeMarksLayout.BlockedIntervals(_inset, _blocked, panels, MarkSize * .5f * _scale, _blockFrom, _blockTo);

            _heroWorld = _driver.GetRenderPosition(Simulation.PlayerId);
            Vector3 hero = _camera.WorldToScreenPoint(_heroWorld + Vector3.up * TargetHeight);
            if (hero.z > 0f) { _heroX = hero.x; _heroY = hero.y; }
            else { _heroX = (_screen.XMin + _screen.XMax) * .5f; _heroY = (_screen.YMin + _screen.YMax) * .5f; }
            // Оси камеры на земле: цель за спиной камеры проецируется по ним, а не через плоскость экрана.
            Transform view = _camera.transform;
            _right = Flat(view.right, Vector3.right);
            _forward = Flat(view.forward, Flat(view.up, Vector3.forward));
            return true;
        }

        static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : fallback;
        }

        // ---------------------------------------------------------------- что метить

        int Collect(RiftRun run, Simulation sim)
        {
            EntityStore e = sim.Entities;
            int count = Mathf.Min(e.Count, _off.Length);
            Array.Clear(_threat, 0, _threat.Length);
            // Атаки, начатые за кадром: действующая метка удара на земле (общий список Sim — в нём и
            // линия шипов Шипомёта, и прыжок Вендиго, и корни Корнехвата)…
            int high = sim.TelegraphHighWater;
            for (int slot = 0; slot < high; slot++)
                if (sim.TryGetTelegraph(slot, out EnemyTelegraph telegraph) && telegraph.IsActive && (uint)telegraph.Source < (uint)count)
                    _threat[telegraph.Source] = true;

            int n = 0;
            for (int i = Simulation.PlayerId + 1; i < count; i++)
            {
                if (!e.Alive[i] || e.Side[i] == Faction.Wole) { _off[i] = false; continue; }
                EnemyKind kind = e.Kind[i];
                // …и разбег Камнекопыта: у тарана метки на земле нет, он виден по замаху и самому бегу.
                if (kind == EnemyKind.ForestStonehoof && sim.TryGetStonehoofAction(i, out StonehoofActionState charge)
                    && (charge.Phase == StonehoofPhase.Windup || charge.Phase == StonehoofPhase.Charge) && !charge.HitResolved)
                    _threat[i] = true;
                bool elite = i == run.BossId || run.Encounters != null && run.Encounters.IsElite(i);
                int priority = WorldEdgeMarksLayout.EnemyPriority(_threat[i], elite, WorldEdgeMarksLayout.IsRanged(kind));
                if (priority == 0) { _off[i] = false; continue; }
                _off[i] = Place(ref n, i, (int)kind, priority, _threat[i], _driver.GetRenderPosition(i), _off[i]);
            }

            // После зачистки — выход и тайники, пока не забраны.
            if (run.Phase == RunPhase.SeekingExit && run.Map != null)
            {
                LayoutMap map = run.Map;
                for (int x = 0; x < map.ExitCount && x < MaxPlaces; x++)
                    _exitOff[x] = Place(ref n, WorldEdgeMarksLayout.ExitKeyBase + x, TagExit, WorldEdgeMarksLayout.ExitPriority, false,
                        Ground(map.ExitPoint(x)), _exitOff[x]);
                for (int b = 0; b < map.RewardBranchCount && b < MaxPlaces; b++)
                {
                    if (run.IsBranchClaimed(b)) { _cacheOff[b] = false; continue; }
                    _cacheOff[b] = Place(ref n, WorldEdgeMarksLayout.CacheKeyBase + b, TagCache, WorldEdgeMarksLayout.CachePriority, false,
                        Ground(map.CenterOf(map.GetRewardBranch(b))), _cacheOff[b]);
                }
            }
            return n;
        }

        static Vector3 Ground(FixVec2 point) => new Vector3(point.X.ToFloat(), LayoutView.ShownFloorLevel(point.X.ToFloat(), point.Y.ToFloat()), point.Y.ToFloat());

        /// <summary>Кандидат метки, если цель за кадром; возвращает «за кадром» для гистерезиса.</summary>
        bool Place(ref int n, int key, int tag, int priority, bool threat, Vector3 world, bool wasOff)
        {
            Vector3 screen = _camera.WorldToScreenPoint(world + Vector3.up * TargetHeight);
            bool front = screen.z > 0f;
            bool off = WorldEdgeMarksLayout.OffScreen(_screen, screen.x, screen.y, front, wasOff, ScreenMargin * _scale, Hysteresis * _scale);
            if (!off || n >= _candidates.Length) return off;
            Vector3 ground = world - _heroWorld;
            ground.y = 0f;
            float dx, dy;
            if (front) { dx = screen.x - _heroX; dy = screen.y - _heroY; }
            else { dx = Vector3.Dot(ground, _right); dy = Vector3.Dot(ground, _forward); }
            WorldEdgeMarksLayout.EdgePoint(_inset, _heroX, _heroY, dx, dy, out float ex, out float ey);
            float s = WorldEdgeMarksLayout.ToPerimeter(_inset, ex, ey);
            s = WorldEdgeMarksLayout.PushOut(s, _perimeter, _blockFrom, _blockTo, _intervals);
            _candidates[n++] = new EdgeMarkCandidate
            {
                Key = key, Tag = tag, Priority = priority, Threat = threat, S = s, Count = 1,
                Distance = ground.magnitude, Angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg,
            };
            return off;
        }

        // ---------------------------------------------------------------- панели HUD

        void FindPanels()
        {
            if ((_combat != null && _runView != null) || Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            if (_combat == null) _combat = FindAnyObjectByType<CombatHudView>(FindObjectsInactive.Include);
            if (_runView == null) _runView = FindAnyObjectByType<RunHudView>(FindObjectsInactive.Include);
        }

        int CollectPanels()
        {
            int n = 0;
            if (_combat != null && _combat.gameObject.activeInHierarchy)
            {
                // Над портретом встаёт строка эффектов (корни, оглушение, зелья): её место тоже занято.
                Panel(ref n, _combat.HeroPanel, PortraitRowReserve, BottomBarSideReserve);
                Panel(ref n, _combat.AbilityPanel, 0f);
                Panel(ref n, _combat.DashPanel, 0f);
                Panel(ref n, _combat.PotionPanel, 0f, BottomBarSideReserve);
                Panel(ref n, _combat.MinimapFrame, 0f);
                Panel(ref n, _combat.MinimapCaptionPanel, 0f);
                if (_combat.ArtifactSlot != null) Panel(ref n, _combat.ArtifactSlot.transform as RectTransform, 0f);
            }
            if (_runView != null && _runView.isActiveAndEnabled)
            {
                Panel(ref n, _runView.Status, 0f);
                Panel(ref n, _runView.Survival, 0f);
                Panel(ref n, _runView.Loot, 0f);
                Panel(ref n, _runView.Boss, 0f);
            }
            return n;
        }

        /// <summary>Прямоугольник панели на экране (холсты HUD — поверх экрана: углы уже в пикселях).</summary>
        void Panel(ref int n, RectTransform rect, float extraTop, float extraSide = 0f)
        {
            if (rect == null || n >= _blocked.Length || !rect.gameObject.activeInHierarchy) return;
            rect.GetWorldCorners(_corners);
            float pad = HudPadding * _scale;
            float xMin = Mathf.Min(_corners[0].x, _corners[2].x), xMax = Mathf.Max(_corners[0].x, _corners[2].x);
            float yMin = Mathf.Min(_corners[0].y, _corners[2].y), yMax = Mathf.Max(_corners[0].y, _corners[2].y);
            float side = extraSide * _scale;
            _blocked[n++] = new EdgeRect(xMin - pad - side, yMin - pad, xMax + pad + side, yMax + pad + extraTop * _scale);
        }

        // ---------------------------------------------------------------- показ

        void Show(int marks, float now, float dt, RiftRun run)
        {
            for (int i = 0; i < _slots.Length; i++) _slots[i].Used = false;
            float radius = MarkSize * .5f;
            float flash = GameUserSettings.FlashScale;
            float wave = .5f + .5f * Mathf.Sin(now * PulseSpeed);
            for (int m = 0; m < marks; m++)
            {
                EdgeMarkCandidate mark = _marks[m];
                Slot slot = Take(mark.Key);
                if (slot == null) continue;
                WorldEdgeMarksItem item = slot.Item;
                bool fresh = slot.Key != mark.Key || !item.gameObject.activeSelf;
                slot.Used = true;
                if (fresh)
                {
                    slot.Key = mark.Key;
                    slot.S = mark.S;
                    slot.Meters = -1;
                    // Подпись — только при первом появлении цели за арену, дальше у края один знак.
                    slot.Labelled = FirstLabel(mark.Key);
                    slot.ShownAt = now;
                }
                else slot.S = WorldEdgeMarksLayout.Glide(slot.S, mark.S, _perimeter, dt, GlideRate, _perimeter * .25f);
                item.Appear();

                WorldEdgeMarksLayout.FromPerimeter(_inset, slot.S, out float x, out float y, out int side);
                ((RectTransform)item.transform).anchoredPosition = new Vector2(x / _scale, y / _scale);
                item.SetLook(mark.Threat ? WorldEdgeMarksItem.Look.Threat : mark.Tag < 0 ? WorldEdgeMarksItem.Look.Goal : WorldEdgeMarksItem.Look.Enemy);
                item.SetIcon(Icon(mark.Tag));
                item.SetCount(mark.Count);
                item.SetChevron(mark.Angle);
                if (mark.Threat)
                    item.SetPulse(PulseGlow * flash * (.35f + .65f * wave), 1f + PulseScale * flash * wave);

                float alpha = slot.Labelled ? WorldEdgeMarksLayout.LabelAlpha(now - slot.ShownAt) : 0f;
                if (alpha > 0f)
                {
                    int meters = WorldEdgeMarksLayout.Meters(mark.Distance);
                    if (meters != slot.Meters || mark.Tag != slot.Tag || slot.Text == null)
                    {
                        slot.Meters = meters;
                        slot.Tag = mark.Tag;
                        slot.Text = WorldEdgeMarksLayout.Label(Name(mark.Tag, mark.Key, run), meters);
                    }
                }
                else if (slot.Labelled && now - slot.ShownAt > 1f) slot.Labelled = false;
                item.SetLabel(alpha, slot.Text, side, radius);
            }
            for (int i = 0; i < _slots.Length; i++)
                if (!_slots[i].Used) _slots[i].Item.Leave();
        }

        /// <summary>Место этой цели (в том числе уходящее — вернётся без нового проявления) или свободное.</summary>
        Slot Take(int key)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (!_slots[i].Used && _slots[i].Key == key && _slots[i].Item.gameObject.activeSelf) return _slots[i];
            for (int i = 0; i < _slots.Length; i++)
                if (!_slots[i].Used && !_slots[i].Item.gameObject.activeSelf) return _slots[i];
            for (int i = 0; i < _slots.Length; i++)
                if (!_slots[i].Used && _slots[i].Item.Leaving) { _slots[i].Item.HideNow(); return _slots[i]; }
            return null;
        }

        void LeaveAll()
        {
            for (int i = 0; i < _slots.Length; i++) _slots[i].Item.Leave();
        }

        bool FirstLabel(int key)
        {
            bool[] flags;
            int index;
            if (key >= WorldEdgeMarksLayout.CacheKeyBase) { flags = _cacheLabelled; index = key - WorldEdgeMarksLayout.CacheKeyBase; }
            else if (key >= WorldEdgeMarksLayout.ExitKeyBase) { flags = _exitLabelled; index = key - WorldEdgeMarksLayout.ExitKeyBase; }
            else { flags = _labelled; index = key; }
            if ((uint)index >= (uint)flags.Length || flags[index]) return false;
            flags[index] = true;
            return true;
        }

        Texture Icon(int tag)
        {
            if (tag == TagExit) return ExitIcon;
            if (tag == TagCache) return CacheIcon;
            Texture icon = KindIcons != null && (uint)tag < (uint)KindIcons.Length ? KindIcons[tag] : null;
            return icon != null ? icon : KindIcons != null && KindIcons.Length > 0 ? KindIcons[0] : null;
        }

        static string Name(int tag, int key, RiftRun run)
        {
            if (tag == TagExit) return "Выход";
            if (tag == TagCache) return "Тайник";
            return key == run.BossId ? EnemyTexts.BossName((EnemyKind)tag) : WorldEdgeMarksLayout.ShortName((EnemyKind)tag);
        }

        /// <summary>Новая арена, сброс или другая симуляция: номера сущностей начались заново.</summary>
        void ResetOnNewArena(Simulation sim, RiftRun run)
        {
            int depth = run != null ? run.Depth : -1;
            int generation = _driver != null ? _driver.Generation : -1;
            if (ReferenceEquals(sim, _shownSim) && generation == _generation && depth == _depth) return;
            _shownSim = sim;
            _generation = generation;
            _depth = depth;
            int capacity = sim != null ? Mathf.Max(sim.Entities.Capacity, sim.Entities.Count) : 0;
            if (_off.Length < capacity)
            {
                _off = new bool[capacity];
                _labelled = new bool[capacity];
                _threat = new bool[capacity];
            }
            else
            {
                Array.Clear(_off, 0, _off.Length);
                Array.Clear(_labelled, 0, _labelled.Length);
            }
            Array.Clear(_exitOff, 0, _exitOff.Length);
            Array.Clear(_exitLabelled, 0, _exitLabelled.Length);
            Array.Clear(_cacheOff, 0, _cacheOff.Length);
            Array.Clear(_cacheLabelled, 0, _cacheLabelled.Length);
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Item.HideNow();
                _slots[i].Key = int.MinValue;
                _slots[i].Text = null;
            }
        }
    }
}
