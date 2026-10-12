# Бросок якоря — контракт Sim → вид (07.10)

Для агентов Unity, которые строят анимации и VFX по одобренным кадрам 07.10
(`ART/characters/pelag/anchor-throw-2026-10-07/chatgpt-results`: база `base-hit-v1` + `base-yank-v1`,
позы `pose-sheet-v1/v2`, Невод `form-net-v1`, Веер `form-fan-ghost-v1`, Гарпун `form-harpoon-v2`).
Sim — `razlom/Assets/Game.Sim/Core/Simulation.AnchorThrow*.cs`. Вид ничего не считает сам: всё ниже —
чтение снимка, событий и чистых функций Sim. Проверка контракта — `Game.Tests/AnchorThrowViewContractTests.cs`.

Обозначения: `C` — тик каста, `s` — `sim.AnchorThrow` (структура `AnchorThrowState`), тик события —
тик шага, на котором оно родилось (`FrameEventContext.SimulationTick − 1`, как в `PelagAnchorThrowFeed`).
Пол — XZ мира = (X, Y) Sim. 30 тиков = 1 с.

## 0. Тайминг (база, 7 м в пустоту, курсор впереди) — 25 тиков, одобрено владельцем 07.10

| Поза листа | Фаза Sim (`s.Phase`) | Тики | Длит. | Событие |
|---|---|---|---|---|
| 1 wind-up | `Windup` | C … C+1 (2; курсор за спиной > 90° — 3) | 67 мс | `AbilityCast` в C |
| 2 release | `Flight` | C+2 = `s.ReleaseTick` | — | `AnchorThrowRelease` |
| 3 pay-out | `Flight` | C+3 … C+9 (7 тиков, 1 м/тик ≈ 28 м/с) | 233 мс | `AnchorThrowHit` по ходу |
| 4 hold | `Taut` | C+10 = `s.HoldTick` … C+11 = `s.TautTick` (2 тика) | 67 мс | `AnchorThrowYank` в C+11 (рывок) |
| 5 yank | `Return` | C+12 … C+17 (`s.ReturnTicks` = 6) | 200 мс | — |
| 6 catch | `Catch` | C+17 = `s.CatchTick`, удержание 2 (C+17, C+18) | 67 мс | `AnchorThrowCatch` |
| 7 ready | `Exit` | C+19 … C+25 (6; ходьба срывает с C+21) | 200 мс | `AnchorThrowEnded` в C+25 |

Итого 25 тиков (0,83 с), герой свободен с C+21 (0,7 с). Формулы: полёт `F = clamp(⌈(R−0,5)/1⌉, 1, 10)`;
натяг `Simulation.AnchorThrowTautTicks` = 2 тика: `HoldTick` = выпуск + F + 1 (голова уже в конце, цепь
прямая, тела стоят), `TautTick` = выпуск + F + 2 — рывок (событие, сеть, тяга со следующего тика);
возврат `clamp(round((R−0,5)/1,1), 4, 6)` по самой длинной полосе (5 м — 4, 6 м — 5, 7 м — 6);
удержание `Simulation.AnchorThrowHoldTicks` = 2, выход 6 (2 без ходьбы). Кривая возврата (голова и тела)
`p(k) = k(T+2k)/(3T²)` от `TautTick` — разгон, рывок в конце (при T = 6: 7 %, 33 % на k = 3, 74 % на k = 5).
Для сравнения сабля: лёгкий удар замах 4 / цикл 8 тиков, финишер 7 / 14, серия из трёх — 30 тиков.

Было до 07.10 (03.10): натяг 1 тик (`TautTick` = выпуск + F + 1), возврат шагом 0,8 м с потолком 8
(7 м — 8 тиков), удержание 3; всего 27, ходьба с C+23. Владелец 07.10 («да, делай»): натяг 1 → 2
(поза «держит» читается), возврат 8 → 6 (рывок резче), удержание 3 → 2. Замах 2 и полёт 7 не тронуты
(«летит сразу»).

## 1. События (Source = герой, ActionVariant = `s.Serial`, кроме `AbilityCast`)

| Событие | Target | Amount | Flag | Position | Следом в том же тике |
|---|---|---|---|---|---|
| `AbilityCast` | −1 | слот | — | герой | — (план броска уже в `s`) |
| `AnchorThrowRelease` | −1 | тики полёта главной полосы | Веер (3 полосы) | рука `s.Origin` | — |
| `AnchorThrowHit` | враг | полоса: 0 якорь, 1 призрак +25° (против часовой), 2 призрак −25°, 3 сеть Невода | пойдёт на тягу | точка касания на оси полосы (у сети — тело) | `Damage`, `Death`, `Stun` |
| `AnchorThrowYank` | цель Гарпуна или −1 | `ReturnTicks` | полёт оборван (стена, босс, Гарпун) | конец главной полосы | `AnchorThrowHit` полосы 3 (сеть) |
| `AnchorThrowCatch` | −1 | сколько тел доехало | — | рука | `Stun` приземления |
| `AnchorThrowEnded` | −1 | `AnchorThrowEnd`: Done / WalkedOut / Interrupted | — | герой | — |

* Тяжёлые, элита (не Гарпун): `Hit.Flag = false`, сразу `Stun` 15 тиков. Босс: `Stun` не приходит никогда.
* Лёгкие: оглушения при попадании нет; `Stun` 15 — в тик ловли, только доехавшим в своей тяге.
* `AnchorThrowYank` — в `TautTick`, последнем тике натяга (рывок), а не в первом: в `HoldTick` событий нет,
  фаза уже `Taut`. Позу «держит» вид начинает с `s.HoldTick` (или с `LaneArriveTick(0) + 1`).
* Укорочение полёта (корпус босса, укус Гарпуна) событием не приходит: в тик касания Sim переписывает
  в снимке `LaneReach/LaneFlight/LaneStop`, `FlightTicks/TautTick/CatchTick` (см. `PelagAnchorThrowFeed.Track`).
* `Interrupted` (рывок, оглушение, смерть, чужой отброс до ловли): тяги сняты, тела стоят где стоят —
  голова падает, цепь сматывается.

## 2. Снимок `s = sim.AnchorThrow` (только чтение)

* Номер и часы: `Serial`, `Phase`, `Form`, `CastTick`, `WindupTicks`, `ReleaseTick`, `FlightTicks`,
  **`HoldTick`** (новое 07.10: первый тик натяга = `TautTick − AnchorThrowTautTicks + 1`), `TautTick`
  (= `YankStartTick`, рывок — последний тик натяга), `ReturnTicks`, `CatchTick` (= `YankEndTick`), `PhaseEndTick`,
  `ExitWalkTick`. До выпуска натяг и ловля — прогноз; с рывка — факт. Фаза `Taut` — `HoldTick … TautTick`.
* Геометрия: `Center` (герой в каст, от него меряются полосы), `Origin` (рука = Center + Dir·0,5),
  `Dir` (к курсору), `Lanes` (1 или 3), `LaneDir(l)`, `LaneReach(l)`, `LaneStep(l)`, `LaneFlight(l)`,
  `LaneStop(l)` (Full / Wall / Boss / Harpoon), **`LaneEnd(l)`**, **`LaneArriveTick(l)`**, `HalfWidth` (0,45 без тела),
  `NetHalfWidth`, **`HasNet`**, `GhostAngle`, `HarpoonTarget` (= `Target`), `Stuck`, **`BiteTick`**,
  `ReeledCount`, `RingRadius`.
* Голова любой полосы на тике: **`Simulation.AnchorThrowHead(in s, lane, tick)`** — до выпуска рука, полёт
  0,5 + шаг·k, в `LaneArriveTick` — ровно конец, до рывка (`TautTick` включительно) ждёт там, возврат по кривой к руке своей полосы,
  с ловли — рука. Между тиками — отрезок (так делает `PelagAnchorThrowVfxRules`). `sim.AnchorThrowAnchorAt` —
  главная голова на последнем шагнувшем тике.

## 3. Кто на крюке

* `sim.AnchorThrowHitLane(id)` — −1 не задет; 0 якорь; 1/2 призраки Веера; 3 сеть.
* `sim.AnchorThrowHitPulls(id)` — помечен «на тягу».
* **`sim.AnchorThrowHooks(int[] ids, bool pulledOnly = false)`** (новое 07.10) — список задетых по
  возрастанию id без аллокаций; возвращает полный счёт (ids == null — только счёт). Для вида, который
  включился посреди броска, съёмки и HUD.
* `sim.AnchorThrowReeled(id)` — тело сейчас в своей тяге (рывок `TautTick` … тик перед ловлей; в `HoldTick`
  ещё false); выпало (смерть, чужой волок) — false, оглушения приземления не будет.
* Всё «задето» живёт до следующего каста (и сбрасывается новой расстановкой).

## 4. Путь тела на рывке

* `sim.AnchorThrowPullFrom(id)` — где стояло в рывок (`TautTick`); `sim.AnchorThrowLandingSpot(id)` — место на
  полукольце перед героем (радиус `s.RingRadius`, вокруг `s.Dir`, без наложений); `sim.AnchorThrowPullLength(id)`.
* Плановая точка на тике: **`Simulation.AnchorThrowPullAt(in s, from, spot, tick)`** — та же кривая, что у
  головы. Настоящая точка — `Entities.Position[id]` (дерево держит, догон до ×1,35 плана тика).
* Для позы и цепи: голова (от конца к руке) обгоняет ближние тела ближе к концу рывка (при 7 м и теле на
  4 м — на ~73 % пути, тик 5 из 6). Если крюк должен «держать» тело до ловли, вид прижимает голову к
  ближайшему телу на крюке — правило Sim это не ломает.

## 5. Формы

* **Невод** (`form-net-v1`, сеть из цепей, морская зелень): `s.HasNet`; прямоугольник ловли —
  **`Simulation.AnchorThrowNetCorners(in s, out nearLeft, out nearRight, out farLeft, out farRight)`**: от руки до
  `LaneEnd(0)`, 3 м поперёк (+ тело врага при проверке); left — +90° от Dir. Ловит один раз — в тик рывка
  `TautTick` (`Hit` полосы 3 сразу за `Yank`), урон 50 %, лёгких тянет на то же полукольцо, тяжёлых и элиту оглушает,
  босса только бьёт. По кадру сеть тянется за головой назад: узкая у якоря, ~3 м у дальнего края,
  дальний край — у пойманных тел. Бросок в стену (дальность < 1 м) — сети нет (`HasNet = false`).
* **Веер** (`form-fan-ghost-v1`, в центре железный якорь, по бокам два призрачных, индиго-фиолет):
  `s.Lanes == 3`, `s.Dir1` = +25° против часовой, `s.Dir2` = −25°; у каждой полосы своя дальность,
  `LaneEnd(l)`, `LaneArriveTick(l)`, своя голова `AnchorThrowHead(s, l, t)`. Враг задет один раз на все полосы
  (полоса — `Hit.Amount`). Натяг и ловля — по самой длинной полосе. 25° на полу = ±17…21° на экране кадра
  (камера орто, наклон 48°).
* **Гарпун** (`form-harpoon-v2`, маджента, борозда за целью): `s.HarpoonTarget`, `s.BiteTick` (= тик `Hit`
  цели), `s.Stuck`; голова остаётся в точке укуса `LaneEnd(0)` = `Hit.Position`; дальше не летит, натяг —
  два следующих тика (держит `BiteTick + 1`, рывок `BiteTick + 2`). `Yank.Target` — цель. Урон ×2. `Hit.Flag` —
  тянется (любой с весом > 0, кроме босса); `Stun` сразу за `Hit` на `2 + ReturnTicks + 30` (натяг, тяга — до
  ловли и ещё 1 с). Босс: `Hit` + `Damage`, ни тяги, ни `Stun`. Вендиго, Шипомёт, манекен — вес 0 (решение
  владельца 29.09, подтверждено 07.10): ×2 и оглушение до ловли + 1 с, стоят, не тянутся.

## 6. Что поменялось в Sim 07.10

* Угол Веера 30° → 25° (`AnchorThrowFanAngle/Cos/Sin`; был заглушкой от «Трёх направлений»).
* Новые accessor-ы только для чтения (`Simulation.AnchorThrow.View.cs`, в хеш не идут): `AnchorThrowHitLane`,
  `AnchorThrowHitPulls`, `AnchorThrowHooks`, `AnchorThrowPullFrom`, `AnchorThrowPullLength`, `AnchorThrowPullAt`,
  `AnchorThrowNetCorners`; в `AnchorThrowState` — `LaneEnd`, `LaneArriveTick`, `BiteTick`, `HasNet`, `HoldTick`.
* **Тайминг 27 → 25 тиков** (§0): `AnchorThrowTautTicks` = 2 (новое), `AnchorThrowReturnStep` 0,8 → 1,1,
  `AnchorThrowMaxReturnTicks` 8 → 6, `AnchorThrowHoldTicks` 3 → 2 (своё число, не Абордажа). Оглушение цели
  Гарпуна — `AnchorThrowTautTicks + R + 30`. Механика и урон не менялись.
* Вид подтянут минимально (клипы и VFX 03.10 всё равно заменяются): `PelagAnchorThrowTimeline` — натяг
  `AnchorThrowTautTicks` в `Release/Yank`, перерасчёт полёта до `HoldTick`; `PelagAnchorThrowClipRules.HoldTicks/
  ExitTicks` — из Sim, `WalkFromFrame` 5 → 4; `PelagAnchorThrowVfxRules.RigPhase` — `Hold` с `HoldTick`.

## 7. Устарело в виде (поправить, когда файл будет в работе)

* Комментарии «±30°» и «водяные призраки» в `Game.View/PelagVfxController.AnchorThrow.cs` (стр. ~22) и
  `PelagVfxController.AnchorThrowForms.cs` (стр. ~8): углы вид берёт из Sim, код верный.
* Невод в коде вида — «вода/пена»; по кадру 07.10 — сеть из цепей.
* ~~Клипы 03.10~~ — заменены 07.10: `Pelag_AN_AnchorThrow2_{Throw, Fly, Yank, Haul, Catch}` по листу поз
  (`ART/characters/pelag/anchor-throw-2026-10-07/animation`, timing.json — кадр = тик §0 при 7 м). Цепочка Throw [0..5] →
  Fly [0..5] → Yank [0..2] → Haul [0..5] → Catch [0..8]: выпуск — Throw 2 в тик выпуска, «держит» — Yank 0 в `HoldTick`,
  рывок — Yank 1 в `TautTick`, пик рывка (поза 5) — Haul 1 на втором тике возврата, ловля — Catch 0 в `CatchTick`,
  «готов» — Catch 6, «якорь на спину» — Catch 7, конец — Catch 8 в тик конца Sim (`PelagAnchorThrowClipRules.KeyChain`).
  Рукоять в левой, якорь бросает и ловит правая (в риге 07.10 пойманный якорь держится в правом кулаке до Catch 7 —
  `PelagAnchorRig.ThrowCatchInRightHand`). Состояния `AnchorThrow_*_v5` прежние, клипы 03.10 остались на диске.
* `AnchorRigThrowPlan.PhaseAt` (риг 03.10) держит `Taut` один тик с `TautTick`; в `HoldTick` у него ещё
  `Flight` (голова уже в конце). `PelagVfxController.AnchorThrow*` (дёрг, натяжение, сеть) считают от `TautTick` —
  это рывок, верно.

## 8. Вопросы владельцу

1. ~~Тайминг~~ — решено 07.10: 25 тиков (§0).
2. ~~«Гарпун тянет даже элиту»~~ — решено 07.10: правило прежнее — элита тянется, Вендиго и Шипомёт (вес 0)
   не тянутся: ×2 и оглушение.
