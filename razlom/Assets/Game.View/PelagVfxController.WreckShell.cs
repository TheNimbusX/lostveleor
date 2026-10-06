using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение v2 — Водяной панцирь (жемчуг #E4EEF6, кадр D-shell): с первого нажатия героя
    /// обволакивают жемчужные ленты воды по силуэту (тела пишут трафарет — вода по телу не рисуется,
    /// герой виден насквозь и не ярче, razlom-no-character-whitening), кружат вокруг; WreckShellHit —
    /// рябь и всплеск на оболочке со стороны удара (кадр D: «удар разбивается брызгами»);
    /// WreckShellBurst — оболочка лопается от силуэта наружу: пласты и капли жемчужной воды летят до
    /// радиуса Sim (2,5 м) и падают внутри него, на земле — только мокрые пятна. Плоского кольца
    /// волны по земле НЕТ — это фигура Обвала Абордажа (правка критика). Не доиграл — тает без взрыва.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class WreckShellRun
        {
            public bool Active;
            public int Fx = -1, Serial;
            public GameObject Object;
            public MeshFilter Filter;
            public ParticleSystem Drops, Sheets, Spots;
            public readonly PelagWreckShellWater Water = new PelagWreckShellWater();
            public float Clock, BurstAt = -1f, MeltAt = -1f;
        }

        private readonly WreckShellRun _wkShell = new WreckShellRun();

        private struct WkSpot
        {
            public float At;
            public Vector3 Position;
        }

        /// <summary>Мокрые пятна там, где упадут пласты взрыва (появляются в миг падения).</summary>
        private readonly WkSpot[] _wkSpots = new WkSpot[16];
        private int _wkSpotCount;

        private void ReleaseWreckShell()
        {
            if (_wkShell.Active && WkStill(_wkShell.Fx, _wkShell.Object)) Release(_wkShell.Fx);
            _wkShell.Active = false;
            _wkShell.Fx = -1;
            _wkShell.Object = null;
        }

        private void ForgetWreckShell()
        {
            ReleaseWreckShell();
            _wkSpotCount = 0;
        }

        /// <summary>Первое нажатие с формой Панциря (тик показа каста).</summary>
        private void BeginWreckShell(in WkPending p)
        {
            ReleaseWreckShell();
            _wkSpotCount = 0;
            int fx = WkSpawn(PelagVfxId.WreckShell, PlayerPosition(), Quaternion.identity, 0f, 6f, PelagForm.WreckShell, out GameObject go);
            if (fx < 0) return;
            go.transform.localScale = Vector3.one;
            _wkShell.Fx = fx;
            _wkShell.Object = go;
            _wkShell.Serial = p.Serial;
            _wkShell.Filter = go.transform.Find("Bands")?.GetComponent<MeshFilter>();
            _wkShell.Drops = go.transform.Find("Drops")?.GetComponent<ParticleSystem>();
            _wkShell.Sheets = go.transform.Find("Sheets")?.GetComponent<ParticleSystem>();
            _wkShell.Spots = go.transform.Find("Spots")?.GetComponent<ParticleSystem>();
            _wkShell.Clock = 0f;
            _wkShell.BurstAt = _wkShell.MeltAt = -1f;
            _wkShell.Water.Begin();
            _wkShell.Active = true;
        }

        /// <summary>Удар по оболочке: рябь со стороны удара и всплеск на ней; отбитый контроль — сильнее.</summary>
        private void PlayWreckShellHit(in WkPending p)
        {
            if (!_wkShell.Active || CaptureRig.NoVfx) return;
            Vector3 hero = PlayerPosition();
            Vector3 side = p.At - hero;
            side.y = 0f;
            if (side.sqrMagnitude < 1e-4f) side = p.Target >= 0 ? FlatDirection(hero, EntityPosition(p.Target, hero + PlayerFacing())) : PlayerFacing();
            side.Normalize();
            _wkShell.Water.Ripple(Mathf.Atan2(side.z, side.x), _wkShell.Clock);
            Vector3 at = hero + side * PelagWreckVfxRules.ShellRadius + Vector3.up * 1.1f;
            WreckBodySplash(at, side + Vector3.up * .3f, p.Flag ? .8f : .6f, PelagForm.WreckShell);
            if (_wkShell.Drops != null)
                for (int i = 0; i < 10; i++)
                    WreckEmit(_wkShell.Drops, at, (side + Random.insideUnitSphere * .6f + Vector3.up * .4f).normalized * Random.Range(1.5f, 3.2f),
                        PelagWreckFormLook.DropColor(PelagForm.WreckShell, Random.Range(.3f, 1f)));
            if (p.Flag) _juice?.PunchCamera(.08f, .03f);
        }

        /// <summary>Лопается в удар оземь: оболочка раздаётся и рвётся, пласты и капли падают внутри радиуса Sim.</summary>
        private void BurstWreckShell(in WkPending p)
        {
            if (!_wkShell.Active || _wkShell.BurstAt >= 0f) return;
            _wkShell.BurstAt = _wkShell.Clock;
            if (CaptureRig.NoVfx) return;
            float radius = p.Extra > 0 ? p.Extra / 100f : Simulation.WreckShellBurstRadius.ToFloat();
            Vector3 hero = PlayerPosition();
            float ground = WreckGroundAt(hero);
            for (int i = 0; i < 36; i++)
            {
                bool sheet = i % 3 == 0;
                ParticleSystem system = sheet ? _wkShell.Sheets : _wkShell.Drops;
                if (system == null) continue;
                float angle = (i + Random.Range(-.4f, .4f)) * (Mathf.PI * 2f / 36f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float height = Random.Range(.4f, 1.7f);
                Vector3 from = hero + radial * PelagWreckVfxRules.ShellRadius + Vector3.up * height;
                float reach = Mathf.Max(.1f, radius * Random.Range(PelagWreckVfxRules.BurstReachMin, PelagWreckVfxRules.BurstReachMax)
                    - PelagWreckVfxRules.ShellRadius);
                float flight = Random.Range(.32f, .5f);
                float g = 9.81f * system.main.gravityModifierMultiplier;
                PelagWreckVfxRules.Launch(reach, flight, from.y - ground, g, out float horizontal, out float vertical);
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = from, velocity = radial * horizontal + Vector3.up * vertical, startLifetime = flight,
                    startColor = PelagWreckFormLook.DropColor(PelagForm.WreckShell, Random.Range(.4f, 1f)), applyShapeToPosition = false
                }, 1);
                if (sheet && _wkSpotCount < _wkSpots.Length)
                {
                    Vector3 land = hero + radial * (PelagWreckVfxRules.ShellRadius + reach);
                    land.y = WreckGroundAt(land) + .03f;
                    _wkSpots[_wkSpotCount++] = new WkSpot { At = Time.time + flight, Position = land };
                }
            }
            _juice?.PunchCamera(.16f, .04f);
        }

        /// <summary>Серия кончилась без удара оземь: оболочка тает без взрыва.</summary>
        private void MeltWreckShell(int serial)
        {
            if (!_wkShell.Active || _wkShell.Serial != serial || _wkShell.BurstAt >= 0f || _wkShell.MeltAt >= 0f) return;
            _wkShell.MeltAt = _wkShell.Clock;
        }

        private void UpdateWreckShell(Simulation sim, float shown, float dt)
        {
            EmitWreckSpots();
            if (!_wkShell.Active) return;
            if (!WkStill(_wkShell.Fx, _wkShell.Object)) { _wkShell.Active = false; _wkShell.Object = null; return; }
            _wkShell.Clock += dt;
            Vector3 feet = PlayerPosition();
            feet.y = WreckGroundAt(feet);
            _wkShell.Object.transform.position = feet;
            float clock = _wkShell.Clock;
            float grow = PelagWreckVfxRules.Smooth01(clock / PelagWreckVfxRules.ShellGrowSeconds);
            float radius = PelagWreckVfxRules.ShellRadius, age = 0f;
            if (_wkShell.BurstAt >= 0f)
            {
                float since = clock - _wkShell.BurstAt;
                radius = PelagWreckVfxRules.ShellBurstRadius(since);
                age = .26f + since * 2.2f;
                // Ленты рвутся за ~0,1 с; объект живёт, пока падают пласты и лежат мокрые пятна.
                if (since > 1.3f) { ReleaseWreckShell(); return; }
            }
            else if (_wkShell.MeltAt >= 0f)
            {
                float since = clock - _wkShell.MeltAt;
                grow *= 1f - PelagWreckVfxRules.Smooth01(since / PelagWreckVfxRules.ShellMeltSeconds);
                age = since * 1.1f;
                if (since > PelagWreckVfxRules.ShellMeltSeconds + .15f) { ReleaseWreckShell(); return; }
            }
            else if (sim.Wreck.Serial != _wkShell.Serial && clock > 1f)
            {
                // Снимок уже другой серии, а конца этой не пришло (смена расстановки) — тает.
                _wkShell.MeltAt = clock;
            }
            if (_wkShell.Filter != null)
                _wkShell.Water.Build(FormWaterMesh.MeshFor(_wkShell.Filter, PelagWreckShellWater.MeshName), feet, radius, grow, clock,
                    age, .2f, .6f);
        }

        private void EmitWreckSpots()
        {
            for (int i = 0; i < _wkSpotCount; i++)
            {
                if (Time.time < _wkSpots[i].At) continue;
                if (_wkShell.Spots != null && !CaptureRig.NoVfx)
                    WreckEmit(_wkShell.Spots, _wkSpots[i].Position, Vector3.zero, PelagWreckFormLook.DropColor(PelagForm.WreckShell, .8f));
                _wkSpots[i--] = _wkSpots[--_wkSpotCount];
            }
        }
    }
}
