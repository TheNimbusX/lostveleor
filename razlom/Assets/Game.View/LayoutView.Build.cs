using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.View
{
    // Сборка арены по кадрам (поток T1, 29.09; владелец: «плавный переход, сейчас подлаг»).
    //
    // Раньше пол, маски земли и декор собирались одним кадром: в сборке 1,8 и 4,2 с под закрытой
    // завесой, дым на это время замирал, а потом прыгал. Теперь сборка — цепочка шагов-итераторов в
    // ТОМ ЖЕ порядке, что и прежде. Пока завеса закрывает мир, шаги идут кусками из общего бюджета
    // кадра (VeilBudget), между кусками кадр рисуется и дым течёт. Тяжёлые попиксельные проходы по
    // маскам 1024² считаются на рабочих потоках над чистыми массивами (каждый пиксель — сам по себе,
    // тот же код и те же числа), SetPixels и Apply — на главном. Итог тот же до бита: сверка — отпечаток
    // LayoutView.Probe (-capture-layout-hash) до и после.
    //
    // Без завесы (редактор, предпросмотры, съёмка без дыма, возврат в лагерь) цепочка проходится
    // сразу, одним вызовом, как раньше. Завеса не расходится, пока сборка не кончилась
    // (CampTransition.Hold ждёт AnyBuilding).
    public sealed partial class LayoutView
    {
        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        private readonly Stack<IEnumerator> _buildStack = new Stack<IEnumerator>();
        private object _buildWait;
        private string _buildLabel = "LayoutView";
        private static int _buildingViews;

        /// <summary>Какая-то арена ещё собирается по кадрам: завеса перехода держится.</summary>
        public static bool AnyBuilding => _buildingViews > 0;

        /// <summary>Сборка этой арены идёт по кадрам.</summary>
        public bool Building => _buildStack.Count > 0;

        /// <summary>
        /// Дособрать всё сразу (запасной выход завесы: сборка не кончилась за отведённое время).
        /// </summary>
        public static void FinishAllBuilds()
        {
            foreach (var view in FindObjectsByType<LayoutView>(FindObjectsInactive.Include))
                view.CompleteBuild();
        }

        private void StartBuild(IEnumerator steps, bool sliced)
        {
            CompleteBuild();
            _buildStack.Push(steps);
            _buildWait = null;
            _buildLabel = "LayoutView";
            _buildingViews++;
            if (sliced) PumpBuild(VeilBudget.Remaining);
            else CompleteBuild();
        }

        /// <summary>Пройти оставшиеся шаги сразу, в этом кадре.</summary>
        private void CompleteBuild()
        {
            if (_buildStack.Count > 0) PumpBuild(-1);
        }

        /// <summary>
        /// Шаги до исчерпания бюджета; budgetMs &lt; 0 — до конца. true — сборка кончилась. Потраченное
        /// списывается с общего бюджета кадра под завесой.
        /// </summary>
        private bool PumpBuild(double budgetMs)
        {
            if (_buildStack.Count == 0) return true;
            if (budgetMs < 0) return PumpSteps(-1, Stopwatch.GetTimestamp());
            if (budgetMs <= 0) return false;
            long start = Stopwatch.GetTimestamp();
            try { return PumpSteps(budgetMs, start); }
            finally { VeilBudget.Spend((Stopwatch.GetTimestamp() - start) * MsPerTick); }
        }

        private bool PumpSteps(double budgetMs, long start)
        {
            bool all = budgetMs < 0;
            while (_buildStack.Count > 0)
            {
                try
                {
                    if (_buildWait != null)
                    {
                        bool ready;
                        using (FrameCost.Measure("LayoutView/ждём потоки")) ready = Ready(_buildWait, all);
                        if (!ready) return false;
                        _buildWait = null;
                    }
                    var top = _buildStack.Peek();
                    bool more;
                    using (FrameCost.Measure(_buildLabel)) more = top.MoveNext();
                    if (!more)
                    {
                        _buildStack.Pop();
                        if (_buildStack.Count == 0) EndBuild();
                        continue;
                    }
                    object current = top.Current;
                    if (current is IEnumerator nested) _buildStack.Push(nested);
                    else if (current != null) _buildWait = current;
                }
                catch (Exception e)
                {
                    // Сборка не должна оставить завесу висеть: ошибка в журнал, остаток шагов отменяется.
                    Debug.LogException(e);
                    AbortBuild();
                    return true;
                }
                if (!all && (Stopwatch.GetTimestamp() - start) * MsPerTick >= budgetMs) return _buildStack.Count == 0;
            }
            return true;
        }

        private static bool Ready(object wait, bool block)
        {
            if (wait is Task task)
            {
                if (!task.IsCompleted)
                {
                    if (!block) return false;
                    task.Wait();
                }
                if (task.Exception != null) throw task.Exception;
                return true;
            }
            // Загрузка из Resources: ждём по кадрам; сразу — чтение asset само дождётся её.
            if (wait is AsyncOperation operation) return block || operation.isDone;
            return true;
        }

        private void EndBuild()
        {
            _buildWait = null;
            if (_buildingViews > 0) _buildingViews--;
            if (FrameCost.Logging) LogGrownPools();
        }

        /// <summary>
        /// Съёмка с журналом кадров: какие пулы сборка растила сверх прогрева — по этим числам поднимать
        /// прогрев вариантов в LayoutView.DecorPrewarm (предупреждение ViewPool говорит только о первом разе).
        /// </summary>
        private void LogGrownPools()
        {
            var line = new System.Text.StringBuilder();
            void Add(ViewPool pool)
            {
                if (pool == null || pool.Created <= pool.PrewarmTarget) return;
                line.Append(line.Length == 0 ? "" : "; ").Append(pool.Name).Append(' ').Append(pool.Created).Append('/').Append(pool.PrewarmTarget);
            }
            Add(_pool);
            for (int i = 0; _decorPools != null && i < _decorPools.Length; i++) Add(_decorPools[i]);
            Debug.Log(line.Length == 0 ? "[frame-log] пулы сборки: все в прогреве" : "[frame-log] пулы сборки сверх прогрева: " + line);
        }

        /// <summary>Отмена без доборки (уничтожение, смена темы): рабочий поток дописывает свои массивы.</summary>
        private void AbortBuild()
        {
            if (_buildWait is Task task)
                try { task.Wait(); } catch (Exception) { }
            // Земля под лугом считается в фоне с середины сборки: массивы не отдаются, пока поток их пишет.
            if (_surfaceWork != null)
                try { _surfaceWork.Wait(); } catch (Exception) { }
            _surfaceWork = null;
            bool active = _buildStack.Count > 0;
            _buildStack.Clear();
            _buildWait = null;
            if (active && _buildingViews > 0) _buildingViews--;
        }

        /// <summary>Метка шага для журнала кадров; как yield — ничего не ждёт.</summary>
        private object Step(string label)
        {
            _buildLabel = label;
            return null;
        }

        /// <summary>Цепочка шагов сразу, до конца (инициализация вне сборки, редакторские пути).</summary>
        private static void RunNow(IEnumerator steps)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(steps);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) stack.Push(nested);
                else if (top.Current != null) Ready(top.Current, true);
            }
        }

        /// <summary>
        /// Попиксельный проход по строкам на рабочих потоках. Строки независимы: каждая пишет только свои
        /// пиксели и читает общие неизменные данные, поэтому порядок потоков на результат не влияет.
        /// </summary>
        private static Task Rows(int count, Action<int> row, string label = null)
            => Task.Run(() =>
            {
                long start = Stopwatch.GetTimestamp();
                Parallel.For(0, count, row);
                if (label != null) FrameCost.Worker(label, start);
            });

        // ---- подготовка в лагере ----
        // Пулы плит и декора, материалы пола и фоновая загрузка префабов декора — в лагере (и под главным
        // меню), кусками не дольше CampWarmSliceMs за кадр, а не в первом кадре сборки арены. Цепочка та же
        // (_initSteps): если вход в разлом случился раньше, сборка под завесой её просто продолжает.
        private const double CampWarmSliceMs = 2;
        private IEnumerator _campWarm;
        private bool _campWarmDone;

        private void WarmInCamp()
        {
            if (!Application.isPlaying || Building || _campWarmDone) return;
            if (_campWarm == null) _campWarm = CampWarmSteps();
            long start = Stopwatch.GetTimestamp();
            try
            {
                using (FrameCost.Measure("LayoutView/подготовка в лагере"))
                    do
                    {
                        if (_campWarm.Current is AsyncOperation load && !load.isDone) return;
                        if (!_campWarm.MoveNext())
                        {
                            _campWarm = null;
                            _campWarmDone = true;
                            return;
                        }
                    }
                    while ((Stopwatch.GetTimestamp() - start) * MsPerTick < CampWarmSliceMs);
            }
            catch (Exception e)
            {
                // Не вышло в лагере — пулы соберёт сама сборка арены, как раньше.
                Debug.LogException(e);
                _campWarm = null;
                _campWarmDone = true;
            }
        }

        private IEnumerator CampWarmSteps()
        {
            if (_initSteps == null && !_initialized) _initSteps = InitializeSteps();
            var steps = _initSteps;
            if (steps != null)
            {
                while (steps.MoveNext()) yield return steps.Current;
                if (_initSteps == steps) _initSteps = null;
            }
            // Пулы плит и декора — до их прогрева (плиты 72, декор — DecorPrewarm: 48, самым частым больше), по объекту за шаг в тех же
            // 2 мс за кадр. Раньше пулы жили с одним объектом, и первая сборка арены досоздавала всё сама:
            // 27 «Пул исчерпан на 1 объектах» за прогон маршрута (плиты и 26 вариантов декора; в бою эти пулы
            // не растут — только в сборке). Вход в разлом раньше конца прогрева — сборка добирает сама, как
            // раньше, и молча (ViewPool.BuildOnly: предупреждение только сверх прогрева — тогда прогрева правда мало,
            // имя варианта в строке); недогретое догреется при следующем возвращении в лагерь. Пересоздание вида
            // (DisposeVisuals) бросает эту цепочку вместе с пулами.
            while (_pool != null && _pool.PrewarmStep(1)) yield return null;
            for (int i = 0; _decorPools != null && i < _decorPools.Length; i++)
                while (_decorPools != null && _decorPools[i] != null && _decorPools[i].PrewarmStep(1)) yield return null;
        }
    }
}

namespace Game.View
{
    /// <summary>
    /// Общий бюджет кадра на работу под дымной завесой: сборку пола и декора (<see cref="LayoutView"/>) и
    /// прогрев тел встречи (<see cref="ArenaView"/>). Вместе с отрисовкой и прочими видами кадр держится
    /// около 40 мс: дым течёт ровно (владелец 29.09: «плавный переход»), а переход не длиннее прежнего —
    /// сборка идёт, пока камера и так доезжает под завесой.
    /// </summary>
    internal static class VeilBudget
    {
        public const double FrameMs = 22;
        private static int _frame = -1;
        private static double _spent;

        /// <summary>Сколько мс работы ещё можно сделать в этом кадре.</summary>
        public static double Remaining
        {
            get
            {
                Sync();
                return Math.Max(0, FrameMs - _spent);
            }
        }

        public static void Spend(double ms)
        {
            Sync();
            _spent += ms;
        }

        private static void Sync()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _spent = 0;
        }
    }
}
