using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Махи Крушения на стеке серии сабли (06.10 вечер; ассеты — Editor/PelagWreckComboVfxSetup, рождение —
    /// PelagVfxController.WreckCombo). Чистые числа вида без Unity: палитра формы (кобальт и сталь у базы, тот же
    /// стек другой краской у форм) и раскладка полумесяца маха.
    ///
    /// Якорная семья отличается от сабли МАТЕРИАЛОМ, не техникой: кобальт и сталь вместо бирюзы, горячая холодная
    /// кромка вместо пены, грани и штрихи вместо пузырьков, искры, сколы и комья вместо капель и клочьев.
    /// Звеньев цепи в волне нет (владелец: нечитаемо, «мыльно») — цепь читается настоящей цепью в 3D.
    /// </summary>
    public static class PelagWreckComboLook
    {
        /// <summary>Палитра волны: глубина у героя, основной тон, светлая сталь, горячая кромка (sRGB, 0…1+), чернила.</summary>
        public struct Palette
        {
            public float DeepR, DeepG, DeepB, MidR, MidG, MidB, LightR, LightG, LightB, HotR, HotG, HotB;
        }

        /// <summary>Чернила обвода — тёмно-синие, как у волны сабли и контуров врагов, не чистый чёрный.</summary>
        public const int InkHex = 0x060B22;

        /// <summary>
        /// Основной тон формы: база — кобальт #2E6FE0; Волнорез #1FB37E, Девятый вал #4B3FD0, Призрачный якорь —
        /// бледно-фиолетовый белый.
        /// </summary>
        public static int MidHex(PelagForm form)
        {
            switch (form)
            {
                case PelagForm.WreckBreakwater: return 0x1FB37E;
                case PelagForm.WreckNinthWave: return 0x4B3FD0;
                case PelagForm.WreckGhostAnchor: return 0xA99CF0;
                default: return 0x2E6FE0;
            }
        }

        /// <summary>Палитра формы: у базы — заданные цвета (#0A1F4A, #2E6FE0, #8FC4FF, почти белый холодный).</summary>
        public static Palette For(PelagForm form)
        {
            var p = new Palette();
            switch (form)
            {
                case PelagForm.WreckBreakwater:
                    Set(0x06302A, 0x1FB37E, 0x8FF0CF, out p.DeepR, out p.DeepG, out p.DeepB, out p.MidR, out p.MidG, out p.MidB,
                        out p.LightR, out p.LightG, out p.LightB);
                    p.HotR = 1.02f; p.HotG = 1.24f; p.HotB = 1.14f;
                    break;
                case PelagForm.WreckNinthWave:
                    Set(0x120C40, 0x4B3FD0, 0xA9A0FF, out p.DeepR, out p.DeepG, out p.DeepB, out p.MidR, out p.MidG, out p.MidB,
                        out p.LightR, out p.LightG, out p.LightB);
                    p.HotR = 1.10f; p.HotG = 1.08f; p.HotB = 1.32f;
                    break;
                case PelagForm.WreckGhostAnchor:
                    Set(0x2C2458, 0xA99CF0, 0xE2DCFF, out p.DeepR, out p.DeepG, out p.DeepB, out p.MidR, out p.MidG, out p.MidB,
                        out p.LightR, out p.LightG, out p.LightB);
                    p.HotR = 1.22f; p.HotG = 1.20f; p.HotB = 1.32f;
                    break;
                default:
                    Set(0x0A1F4A, 0x2E6FE0, 0x8FC4FF, out p.DeepR, out p.DeepG, out p.DeepB, out p.MidR, out p.MidG, out p.MidB,
                        out p.LightR, out p.LightG, out p.LightB);
                    p.HotR = .92f; p.HotG = 1.06f; p.HotB = 1.40f;
                    break;
            }
            return p;
        }

        private static void Set(int deep, int mid, int light, out float dr, out float dg, out float db,
            out float mr, out float mg, out float mb, out float lr, out float lg, out float lb)
        {
            PelagWreckSwingLook.Rgb(deep, out dr, out dg, out db);
            PelagWreckSwingLook.Rgb(mid, out mr, out mg, out mb);
            PelagWreckSwingLook.Rgb(light, out lr, out lg, out lb);
        }

        // ---------------------------------------------------------------- раскладка маха

        /// <summary>
        /// Высота плоскости маха над землёй, м (индекс 0 — мах 1, 1 — мах 2 и «Четвёртый»): голова якоря на цепи идёт
        /// чуть ниже плеч на махе 1 и у пояса на обратном махе 2.
        /// </summary>
        public static readonly float[] Height = { 1.02f, .90f };

        /// <summary>Крен плоскости вокруг оси удара, град (+ — голова полумесяца ниже хвоста), как у волны сабли.</summary>
        public static readonly float[] Roll = { 8f, 6f };

        /// <summary>
        /// Наружный край — доля сектора махов Sim (Radius сборки, 2,8 м): на 0,82 сталь идёт перед телами задетых и
        /// касается их (как у сабли); мах 2 чуть больше.
        /// </summary>
        public static readonly float[] Reach = { .82f, .87f };

        /// <summary>Центр полумесяца позади героя, м: тупые концы обнимают его бока.</summary>
        public const float Back = .15f;

        /// <summary>Наружный радиус полумесяца, м, по сектору Sim.</summary>
        public static float Radius(int side, float sector) => sector * Reach[side] + Back;

        /// <summary>Знак на задетом: масштаб лёгкого (мах 1) и тяжёлого (мах 2, «Четвёртый»).</summary>
        public static readonly float[] HitScale = { 1f, 1.25f };
    }
}
