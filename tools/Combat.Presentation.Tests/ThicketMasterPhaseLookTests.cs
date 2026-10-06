using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

/// <summary>
/// Хозяин Чащи — облик фаз и силуэт (ревью владельца 02.10 вечер, находки 7 и 9): проверки по
/// исходникам, которые без Unity не запустить.
/// • Лунная кромка (RazlomSeeThrough.hlsl, _RazlomBossRim) — в проходе ForwardLit обоих шейдеров
///   босса, и это не свойство материала: без блока свойств вида кромки нет (шейдер — как URP).
/// • Одежда фаз (ThicketMasterPhaseDressing) после перезагрузки домена в Play не падала бы:
///   кэш вида не сериализуется, блок свойств создаётся только под проверкой на null (не в кадре).
/// </summary>
public sealed class ThicketMasterPhaseLookTests
{
    private static string Assets => Path.Combine(RepoRoot.Path, "razlom", "Assets");
    private static string Shaders => Path.Combine(Assets, "Resources", "Shaders");

    [Test]
    public void BossShaders_AddTheMoonRim_OnlyInForwardLit()
    {
        foreach (string name in new[] { "RazlomBossSeeThroughLit.shader", "RazlomBossSeeThroughSimpleLit.shader" })
        {
            // Без комментариев: в шапке шейдера кромка упомянута словами.
            string text = Regex.Replace(File.ReadAllText(Path.Combine(Shaders, name)), @"//[^\n]*", "");
            int calls = Regex.Matches(text, @"RazlomBossRim\s*\(").Count;
            Assert.AreEqual(1, calls, name + ": кромка добавляется один раз — в ForwardLit");
            int forward = text.IndexOf("Name \"ForwardLit\"", System.StringComparison.Ordinal);
            int shadow = text.IndexOf("Name \"ShadowCaster\"", System.StringComparison.Ordinal);
            int call = Regex.Match(text, @"RazlomBossRim\s*\(").Index;
            Assert.That(forward >= 0 && shadow > forward && call > forward && call < shadow, name + ": вызов — внутри прохода ForwardLit");
            // Не свойство материала: в Properties его нет, значение — только из блока свойств вида.
            string properties = text.Substring(0, text.IndexOf("SubShader", System.StringComparison.Ordinal));
            Assert.That(!properties.Contains("_RazlomBossRim"), name + ": _RazlomBossRim не в Properties");
        }
        string include = File.ReadAllText(Path.Combine(Shaders, "RazlomSeeThrough.hlsl"));
        Assert.That(include.Contains("float4 _RazlomBossRim;"), "кромка — глобальная переменная include, не CBUFFER материала");
        Assert.That(!Regex.IsMatch(include, @"CBUFFER_START[\s\S]*_RazlomBossRim[\s\S]*CBUFFER_END"), "кромка вне UnityPerMaterial");
    }

    [Test]
    public void PhaseDressing_CacheIsNotSerialized_AndTheBlockIsCreatedOnce()
    {
        string path = Path.Combine(Assets, "Game.View", "ThicketMasterPhaseDressing.cs");
        string[] lines = File.ReadAllLines(path);
        // Поле экземпляра: «private Тип _имя…;» без static/const и без скобок метода.
        var field = new Regex(@"^\s*(\[NonSerialized\]\s*)?private\s+(?!static\b|const\b|sealed\b|void\b)[\w\.\[\]<>,\s]+\s+_\w+[^()]*;\s*$");
        var fields = lines.Where(l => field.IsMatch(l)).ToArray();
        Assert.That(fields.Length, Is.GreaterThan(10), "поля кэша найдены");
        foreach (string line in fields)
            Assert.That(line.TrimStart().StartsWith("[NonSerialized]"), "не сериализуется при перезагрузке домена: " + line.Trim());

        // Блок свойств: только под проверкой на null (после перезагрузки домена он null; в кадре — без аллокаций).
        foreach (string line in lines.Where(l => l.Contains("new MaterialPropertyBlock")))
            Assert.That(line.Contains("_block == null"), "блок создаётся только если его нет: " + line.Trim());
        string text = File.ReadAllText(path);
        Assert.That(Regex.IsMatch(text, @"void OnEnable\(\)\s*=>\s*EnsureInitialized\(\)"), "после перезагрузки домена (без Awake) кэш собирается в OnEnable");
    }
}
