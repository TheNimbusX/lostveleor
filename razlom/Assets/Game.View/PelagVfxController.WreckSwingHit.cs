using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Махи Крушения «холодное железо», вид V2 — удар и знак на задетом (swing1.png / swing2.png, числа —
    /// PelagWreckSwingImpact, цвета — PelagWreckSwingLook). В удар маха — искры с головы якоря и толчок камеры.
    /// Задетый (каждый Damage маха: Sim бьёт врага в тик, когда голова проходит его угол) — короткий росчерк ПО ХОДУ
    /// маха (тёмный контур, тело в цвет формы, сердцевина к белому; у маха 2 — ещё тонкие сбоку), сколы железа
    /// (лист обломков пака, тёмное железо с кромкой формы) по ходу и вверх, клуб пыли у ног, отброс тела по весу
    /// маха (вид; Sim махом не толкает). Шара-вспышки нет. Всё — из очереди событий Sim, не опросом.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Этап последнего удара серии (вес знака без этапа Sim) и форма серии (строка таблицы цветов).</summary>
        private int _wsLastStage = -1;
        private PelagForm _wsLastForm;

        /// <summary>Секторы махов 1 и 2 текущей серии (Simulation.TryGetWreckSweep отдаёт только последний мах).</summary>
        private int _wsSweepSerial = -1;
        private readonly int[] _wsSweepFrom = { int.MaxValue, int.MaxValue }, _wsSweepTo = { int.MinValue, int.MinValue };

        /// <summary>Кого мах 1 серии уже бил: серия по номеру тела (стык махов — SweepStageAt).</summary>
        private int[] _wsHitBySwing1 = new int[64];

        /// <summary>Запомнить сектор последнего маха Sim (каждый кадр серпа и каждый Damage Крушения).</summary>
        private void NoteWreckSweep(Simulation sim)
        {
            if (sim == null || !sim.TryGetWreckSweep(out int stage, out int serial, out _, out int from, out int to) || (uint)stage > 1u) return;
            if (serial != _wsSweepSerial)
            {
                _wsSweepSerial = serial;
                _wsSweepFrom[0] = _wsSweepFrom[1] = int.MaxValue;
                _wsSweepTo[0] = _wsSweepTo[1] = int.MinValue;
            }
            _wsSweepFrom[stage] = from;
            _wsSweepTo[stage] = to;
        }

        /// <summary>
        /// Этап маха Sim для Damage тика <paramref name="tick"/> (TakeWreckDamage): удары хода головы вне тика контакта
        /// ClassifyHit отдаёт как Other — здесь они становятся махом своего этапа. −1 — не мах.
        /// </summary>
        private int WreckSweepHitStage(Simulation sim, int tick, int target, ref WreckVfxHit hit)
        {
            NoteWreckSweep(sim);
            if (target >= _wsHitBySwing1.Length) System.Array.Resize(ref _wsHitBySwing1, Mathf.NextPowerOfTwo(target + 1));
            bool hitBefore = target >= 0 && _wsHitBySwing1[target] == _wsSweepSerial + 1;
            int sweep = PelagWreckSwingRules.SweepStageAt(tick, _wsSweepFrom[0], _wsSweepTo[0], _wsSweepFrom[1], _wsSweepTo[1], hitBefore);
            hit = PelagWreckSwingRules.SweepHit(hit, sweep, out int stage);
            if (stage == PelagWreckSwingRules.StageSwing && target >= 0) _wsHitBySwing1[target] = _wsSweepSerial + 1;
            return stage;
        }

        /// <summary>
        /// Удар маха или «Четвёртого» (из PlayWreckContact, путь «железа»). True — разобрано: искры с головы,
        /// толчок камеры у махов (у «Четвёртого» толкает его круг). False — махи «железом» не собраны.
        /// </summary>
        private bool PlayWreckSwingContact(WreckArcRun arc, in WkPending p)
        {
            if (!WreckSwingReady || !PelagWreckSwingRules.Draws(p.Stage)) return false;
            _wsLastStage = p.Stage;
            WreckSwingRun run = _wsNow != null && _wsNow.Active && _wsNow.Serial == p.Serial && _wsNow.Stage == p.Stage ? _wsNow : null;
            if (CaptureRig.NoVfx) return true;
            PelagWreckSwingImpact.Numbers n = PelagWreckSwingImpact.For(PelagWreckSwingRules.WeightOf(p.Stage));
            if (run != null && run.HasHead && run.Glints != null)
            {
                Vector3 along = run.Velocity.sqrMagnitude > 1f ? run.Velocity.normalized : PlayerFacing();
                Color light = WsColor(PelagWreckSwingLook.Light(run.Form));
                for (int i = 0; i < n.ContactGlints; i++)
                    WiEmit(run.Glints, run.Head, (along + Random.insideUnitSphere * .6f + Vector3.up * .15f).normalized * Random.Range(4f, 8f),
                        Color.Lerp(light, Color.white, i % 2 == 0 ? .8f : .45f), Random.Range(.04f, .07f), Random.Range(.12f, .22f));
            }
            if (p.Stage < PelagWreckSwingRules.StageSlam) _juice?.PunchCamera(n.PunchTrauma, n.PunchZoom);
            return true;
        }

        /// <summary>
        /// Задетый махом или «Четвёртым» (из PlayWreckHit, путь «железа»). True — нарисовано здесь (росчерк, сколы,
        /// пыль, отброс по весу); False — не мах или махи «железом» не собраны (прежняя искра «железа»).
        /// </summary>
        private bool PlayWreckSwingHit(in WkPending p, Vector3 body, Vector3 dir)
        {
            if (!WreckSwingReady) return false;
            int stage = p.Hit == WreckVfxHit.Fourth ? PelagWreckSwingRules.StageFourth : p.Stage >= 0 ? p.Stage : _wsLastStage;
            WreckSwingWeight weight = PelagWreckSwingRules.HitWeight(p.Hit, stage);
            if (weight == WreckSwingWeight.None) return false;
            PelagWreckSwingImpact.Numbers n = PelagWreckSwingImpact.For(weight);
            // Ход маха у задетого: касательная по стороне маха Sim его этапа (мах 2 — слева направо).
            Vector3 hero = PlayerPosition();
            Vector3 radial = body - hero;
            PelagWreckSwingRules.SweepDirection(radial.x, radial.z, stage, out float tx, out float tz);
            var along = new Vector3(tx, 0f, tz);
            dir.y = 0f;
            if (along.sqrMagnitude > .25f) dir = along;
            else if (dir.sqrMagnitude < 1e-4f) dir = FlatDirection(hero, body);
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : PlayerFacing();
            // Отброс тела по ходу маха (PlayWreckHit мог взять ход прошлого маха), сдвиг — по весу маха.
            WreckRecoil(p.Target, dir);
            ScaleWreckRecoil(p.Target, PelagWreckSwingImpact.RecoilScale(weight));
            if (CaptureRig.NoVfx) return true;

            Simulation sim = _driver.Sim;
            float radius = sim != null && (uint)p.Target < (uint)sim.Entities.Count
                ? sim.Entities.BodyRadius[p.Target].ToFloat() : EntityStore.DefaultBodyRadius.ToFloat();
            // 06.10: знак — на технике серии сабли (.WreckIronSwing: звезда, искры, сколы, пыль); ниже — прежний росчерк V2.
            // 06.10 вечер: рисованные звезда и сколы (.WreckPainted); без их сборки — знак «железа».
            // 06.10 поздно: знак на стеке серии сабли (.WreckCombo: вспышка, искры, сколы, комья) — первым.
            if (PlayWreckComboMark(body, dir, radius, weight) || PlayWreckPaintedMark(body, dir, radius, weight)
                || PlayWreckIronSwingMark(body, dir, radius, weight)) return true;
            float groundY = WreckGroundAt(body);
            var foot = new Vector3(body.x, groundY + .03f, body.z);
            Vector3 chest = foot + Vector3.up * Mathf.Clamp(.55f + .5f * radius, .7f, 1.4f);
            Vector3 front = chest;
            Camera camera = Camera.main;
            if (camera != null) front += (camera.transform.position - chest).normalized * (.25f + .3f * radius);
            if (WiSpawn(PelagVfxId.WreckSwingHit, foot, Quaternion.LookRotation(dir, Vector3.up), 0f, 1.1f, out GameObject go) < 0) return true;
            go.transform.localScale = Vector3.one;
            Transform root = go.transform;
            PelagWreckSwingLook.Row look = PelagWreckSwingLook.For(_wsLastForm);
            Color light = WsColor(look.Light), ink = WsColor(PelagWreckSwingLook.OutlineHex, .9f), hot = WsColor(PelagWreckSwingLook.HotHex);
            WreckSwingStreak(root, front, dir, n, light, ink, hot);
            WreckSwingChips(root.Find("Chips")?.GetComponent<ParticleSystem>(), chest, dir, n, light);
            ParticleSystem dust = root.Find("Dust")?.GetComponent<ParticleSystem>();
            var side = new Vector3(-dir.z, 0f, dir.x);
            for (int i = 0; i < n.Dust; i++)
                WiEmit(dust, foot + dir * .2f + side * Random.Range(-.3f, .3f) + Vector3.up * .1f,
                    dir * Random.Range(.4f, .9f) + Vector3.up * Random.Range(.2f, .4f), WiDust, Random.Range(n.DustSizeMin, n.DustSizeMax), n.DustSeconds);
            return true;
        }

        /// <summary>
        /// Росчерк по ходу маха: «Ink» — тёмный контур (шире), «Streak» — тело в цвет формы, «Core» — сердцевина к белому;
        /// у тяжёлого — тонкие росчерки сбоку. Растянутые частицы летят по ходу и гаснут за StreakSeconds.
        /// </summary>
        private static void WreckSwingStreak(Transform root, Vector3 at, Vector3 dir, in PelagWreckSwingImpact.Numbers n, Color light, Color ink, Color hot)
        {
            ParticleSystem inkLayer = root.Find("Ink")?.GetComponent<ParticleSystem>();
            ParticleSystem body = root.Find("Streak")?.GetComponent<ParticleSystem>();
            ParticleSystem core = root.Find("Core")?.GetComponent<ParticleSystem>();
            float size = PelagWreckSwingImpact.StreakStartSize(n.StreakLength);
            float speed = PelagWreckSwingImpact.StreakSpeed(n);
            Vector3 start = at - dir * (n.StreakLength * .35f);
            WiEmit(inkLayer, start, dir * speed, ink, size * (1f + PelagWreckSwingImpact.InkGrow), n.StreakSeconds * 1.15f);
            WiEmit(body, start, dir * speed, light, size, n.StreakSeconds);
            WiEmit(core, start + dir * (n.StreakLength * .08f), dir * speed, hot, size * PelagWreckSwingImpact.CoreShare, n.StreakSeconds * .8f);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            for (int i = 0; i < n.StreakSlivers; i++)
            {
                float sign = (i & 1) == 0 ? 1f : -1f;
                Vector3 d = Quaternion.AngleAxis(sign * PelagWreckSwingImpact.SliverDegrees, Vector3.up) * dir;
                Vector3 p = start + side * (sign * PelagWreckSwingImpact.SliverOffset);
                float s = size * PelagWreckSwingImpact.SliverLength;
                WiEmit(inkLayer, p, d * speed, ink, s * (1f + PelagWreckSwingImpact.InkGrow), n.StreakSeconds);
                WiEmit(body, p, d * speed, light, s, n.StreakSeconds * .85f);
            }
        }

        /// <summary>Сколы железа: лист обломков пака (тело — тёмное железо материала, кромка — цвет частицы), по ходу и вверх.</summary>
        private static void WreckSwingChips(ParticleSystem chips, Vector3 at, Vector3 dir, in PelagWreckSwingImpact.Numbers n, Color rim)
        {
            if (chips == null) return;
            for (int i = 0; i < n.Chips; i++)
            {
                Vector3 d = (dir * 1.1f + Vector3.up * .55f + Random.insideUnitSphere * .55f).normalized;
                WiEmit(chips, at + Random.insideUnitSphere * .12f, d * Random.Range(n.ChipSpeedMin, n.ChipSpeedMax), rim,
                    Random.Range(n.ChipSizeMin, n.ChipSizeMax), Random.Range(n.ChipSecondsMin, n.ChipSecondsMax));
            }
        }

        /// <summary>
        /// «Четвёртый удар» Волнореза и Девятого вала: земля вокруг героя — прежняя пена (веер Пенных волн и корона,
        /// как было в PlayWreckContact), пока их формы не переделаны в «железо»; мах — уже серп.
        /// </summary>
        private void PlayWreckFoamFourth(Vector3 hero)
        {
            Vector3 foot = new Vector3(hero.x, WreckGroundAt(hero) + .02f, hero.z);
            WkSpawn(PelagVfxId.WreckBurst, foot, Quaternion.LookRotation(PlayerFacing(), Vector3.up), 1.1f, 0f, PelagForm.None, out _);
            WreckCrown(foot, PlayerFacing(), 1.2f, PelagForm.None);
            _juice?.PunchCamera(.26f, .05f);
            PulseCombatLight(.45f);
        }

        /// <summary>Отдача тела, только что заданная WreckRecoil: сдвиг × <paramref name="scale"/> (направление — единичное).</summary>
        private void ScaleWreckRecoil(int target, float scale)
        {
            if (target < 0) return;
            for (int i = 0; i < _wkBodies.Length; i++)
                if (_wkBodies[i].Active && _wkBodies[i].Target == target)
                {
                    Vector3 d = _wkBodies[i].RecoilDir;
                    _wkBodies[i].RecoilDir = d.sqrMagnitude > 1e-6f ? d.normalized * scale : d;
                    return;
                }
        }
    }
}
