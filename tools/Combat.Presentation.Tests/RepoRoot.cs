using System.IO;
using System.Runtime.CompilerServices;
using NUnit.Framework;

/// <summary>
/// Корень репозитория для тестов, читающих файлы проекта. Сначала — вверх от
/// папки сборки тестов; сборка с --artifacts-path лежит вне репозитория (так
/// потоки собираются изолированно), тогда — вверх от папки этого исходника.
/// </summary>
internal static class RepoRoot
{
    public static string Path
    {
        get
        {
            string found = Find(TestContext.CurrentContext.TestDirectory) ?? Find(SourceDirectory());
            Assert.That(found, Is.Not.Null, "не найден корень репозитория");
            return found;
        }
    }

    private static string Find(string start)
    {
        var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
        while (dir != null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "razlom", "Assets"))) dir = dir.Parent;
        return dir?.FullName;
    }

    private static string SourceDirectory([CallerFilePath] string file = "") => System.IO.Path.GetDirectoryName(file);
}
