using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Game.LocationEditor
{
    [InitializeOnLoad]
    public static class LocationTestRunner
    {
        private static TestRunnerApi _api;
        private static double _nextCheck;
        // A scoped request file also allows verification when Unity owns the
        // project lock and cannot be started a second time in batch mode.
        public const string RequestPath = "Library/LocationEditMode.request";

        static LocationTestRunner() => EditorApplication.update += CheckRequest;

        private static void CheckRequest()
        {
            if (EditorApplication.timeSinceStartup < _nextCheck) return;
            _nextCheck = EditorApplication.timeSinceStartup + 1;
            if (_api != null || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
            File.Delete(RequestPath);
            MeadowLocationAssets.EnsureCreated();
            RunAll();
        }

        [MenuItem("Разлом/Локации/Проверить все EditMode-тесты %&F8", priority = 50)]
        public static void RunAll()
        {
            if (_api != null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _api.RegisterCallbacks(new Results());
            _api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                Directory.CreateDirectory("Logs");
                TestRunnerApi.SaveResultToFile(result, "Logs/LocationEditMode.xml");
                string summary = $"[Локации] EditMode: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped. Logs/LocationEditMode.xml";
                if (result.PassCount + result.FailCount == 0) Debug.LogError("Ни один тест не выполнен. " + summary);
                else Debug.Log(summary);
                Object.DestroyImmediate(_api);
                _api = null;
            }
        }
    }
}
