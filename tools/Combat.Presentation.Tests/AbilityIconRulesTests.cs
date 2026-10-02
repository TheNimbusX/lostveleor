using System.Collections.Generic;
using System.IO;
using Game.Sim;
using Game.View;
using NUnit.Framework;

// Правило иконки способности (план форм 02.10, §7): одно место (способность, форма) → файл, ключ кешей с формой,
// метка формы на базовой иконке, пока нет своего арта. Без Unity — сама картинка собирается в AbilityIcons.
public sealed class AbilityIconRulesTests
{
    static IEnumerable<int> PoolDefinitionIds()
    {
        for (int i = 0; i < PelagKit.PoolSize; i++)
        {
            AbilityDefinition definition = PelagKit.PoolDefinition(i);
            if (definition != null) yield return definition.Id;
        }
        yield return AbilityDefinition.DashId;
    }

    static IEnumerable<PelagForm> TableForms()
    {
        for (int f = 1; f <= PelagForms.Count; f++) yield return (PelagForm)f;
    }

    [Test]
    public void BaseIcon_EverySkillOfThePoolAndDash_HasItsFileOnDisk()
    {
        string folder = Path.Combine(RepoRoot.Path, "razlom", "Assets", "Resources", "UI", "Abilities");
        foreach (int id in PoolDefinitionIds())
        {
            string file = AbilityIconRules.BaseFile(id);
            Assert.That(file, Is.Not.Null, "у способности " + id + " нет иконки");
            Assert.That(File.Exists(Path.Combine(folder, file + ".png")), Is.True, file + ".png нет в Resources/UI/Abilities");
        }
    }

    [Test]
    public void BaseIcon_KeepsTheNamesTheHudUsedBeforeForms()
    {
        // Перенос из PlayerHud.IconFile: имена файлов те же, PlayerHud.IconFile — обёртка над правилом.
        Assert.That(AbilityIconRules.BaseFile(AbilityDefinition.WhirlwindId), Is.EqualTo("Icon_Whirlwind"));
        Assert.That(AbilityIconRules.BaseFile(AbilityDefinition.ChainStepId), Is.EqualTo("Icon_Squall"));
        Assert.That(AbilityIconRules.BaseFile(AbilityDefinition.AnchorSlamId), Is.EqualTo("Icon_AnchorSweep"));
        Assert.That(AbilityIconRules.BaseFile(AbilityDefinition.AnchorLeapId), Is.EqualTo("Icon_AnchorLeap"));
        Assert.That(AbilityIconRules.BaseFile(AbilityDefinition.DashId), Is.EqualTo("Icon_Dash"));
        Assert.That(AbilityIconRules.BaseFile(-12345), Is.Null);
    }

    [Test]
    public void NoForm_IsTheBaseIcon_AndHasItsOwnCacheKey()
    {
        int whirlwind = AbilityDefinition.WhirlwindId;
        Assert.That(AbilityIconRules.FormFile(whirlwind, PelagForm.None), Is.Null, "без формы — базовая иконка");
        Assert.That(AbilityIconRules.FormSuffix(PelagForm.None), Is.Null);
        foreach (PelagForm form in TableForms())
            Assert.That(AbilityIconRules.CacheKey(whirlwind, form), Is.Not.EqualTo(AbilityIconRules.CacheKey(whirlwind, PelagForm.None)),
                "кеш по одному DefinitionId не сменил бы иконку на форму " + form);
    }

    [Test]
    public void FormArt_IsSearchedUnderSkillAndFormName()
    {
        int whirlwind = AbilityDefinition.WhirlwindId;
        Assert.That(AbilityIconRules.FormFile(whirlwind, PelagForm.WhirlwindStorm), Is.EqualTo("Icon_Whirlwind_Storm"));
        Assert.That(AbilityIconRules.FormFile(whirlwind, PelagForm.WhirlwindMaelstrom), Is.EqualTo("Icon_Whirlwind_Maelstrom"));
        Assert.That(AbilityIconRules.FormFile(whirlwind, PelagForm.WhirlwindFoamWaves), Is.EqualTo("Icon_Whirlwind_FoamWaves"));
        Assert.That(AbilityIconRules.FormFile(whirlwind, PelagForm.WhirlwindOnTheMove), Is.EqualTo("Icon_Whirlwind_OnTheMove"));
        // Номер формы без своей строки всё равно получает свой файл — две формы не делят одну картинку.
        Assert.That(AbilityIconRules.FormSuffix((PelagForm)200), Is.EqualTo("Form200"));
    }

    [Test]
    public void EveryFormOfTheTable_HasItsOwnFileAndCacheKey()
    {
        var files = new HashSet<string>();
        var keys = new HashSet<long>();
        foreach (int id in PoolDefinitionIds())
        {
            Assert.That(keys.Add(AbilityIconRules.CacheKey(id, PelagForm.None)), Is.True);
            foreach (PelagForm form in TableForms())
                Assert.That(keys.Add(AbilityIconRules.CacheKey(id, form)), Is.True, "ключ кеша совпал: " + id + " / " + form);
        }
        foreach (PelagForm form in TableForms())
        {
            AbilityDefinition definition = PelagKit.PoolDefinition(PelagForms.LineOf(form));
            Assert.That(definition, Is.Not.Null, form + " — форма линии без способности");
            string file = AbilityIconRules.FormFile(definition.Id, form);
            Assert.That(file, Does.StartWith(AbilityIconRules.BaseFile(definition.Id) + "_"));
            Assert.That(files.Add(file), Is.True, "две формы с одним файлом: " + file);
        }
    }

    // ---------------------------------------------------------------- метка формы

    [Test]
    public void Mark_SitsLowerRight_InsideTheCircleTheHudShows()
    {
        Assert.That(AbilityIconRules.MarkCenterU, Is.GreaterThan(.5f), "метка справа");
        Assert.That(AbilityIconRules.MarkCenterV, Is.LessThan(.5f), "метка внизу (строки текстуры снизу вверх)");
        // Плитка HUD: иконка обрезана на IconCrop с каждой стороны и спрятана в круг; маска ещё на 3 из 76 единиц внутри.
        float hudCircle = AbilityIconRules.HudVisibleRadius - 3f / 76f * (1f - 2f * AbilityIconRules.HudIconCrop);
        Assert.That(AbilityIconRules.MarkFarthest, Is.LessThanOrEqualTo(hudCircle), "круг метки срезан маской плитки HUD");
        // Медальон карточки (маска на 4% внутри круга иконки).
        Assert.That(AbilityIconRules.MarkFarthest, Is.LessThan(.46f));
        // Метка — знак, а не заплатка: не больше пятой части стороны иконки.
        Assert.That(AbilityIconRules.MarkRadius * 2f, Is.LessThanOrEqualTo(.2f));
    }

    [Test]
    public void Mark_Layers_DarkDiscAccentRingAndCore()
    {
        float u = AbilityIconRules.MarkCenterU, v = AbilityIconRules.MarkCenterV, pixel = 1f / AbilityIconRules.MarkedIconSize;
        AbilityIconRules.MarkCover centre = AbilityIconRules.Mark(u, v, pixel);
        Assert.That(centre.Disc, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(centre.Core, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(centre.Ring, Is.EqualTo(0f));

        AbilityIconRules.MarkCover ring = AbilityIconRules.Mark(u + AbilityIconRules.MarkRadius * .8f, v, pixel);
        Assert.That(ring.Ring, Is.EqualTo(1f).Within(1e-4f), "кольцо акцента у кромки круга");
        Assert.That(ring.Core, Is.EqualTo(0f));

        AbilityIconRules.MarkCover far = AbilityIconRules.Mark(.5f, .5f, pixel);
        Assert.That(far.Any, Is.False, "середина иконки — без метки");
        AbilityIconRules.MarkPixels(AbilityIconRules.MarkedIconSize, out int x0, out int y0, out int x1, out int y1);
        Assert.That(x0, Is.GreaterThan(AbilityIconRules.MarkedIconSize / 2), "метка рисуется только в своём углу");
        Assert.That(y1, Is.LessThanOrEqualTo(AbilityIconRules.MarkedIconSize / 2));
    }

    [Test]
    public void Paint_ChangesOnlyTheMark_InTheKitColours()
    {
        float pixel = 1f / AbilityIconRules.MarkedIconSize;
        byte r = 200, g = 210, b = 220;
        AbilityIconRules.Paint(ref r, ref g, ref b, .3f, .7f, pixel);
        Assert.That(new[] { r, g, b }, Is.EqualTo(new byte[] { 200, 210, 220 }), "пиксель вне метки изменился");

        float u = AbilityIconRules.MarkCenterU, v = AbilityIconRules.MarkCenterV;
        // Между огоньком и кольцом — тёмный круг (дым темы) поверх светлой картинки.
        r = g = b = 255;
        AbilityIconRules.Paint(ref r, ref g, ref b, u + AbilityIconRules.MarkRadius * .55f, v, pixel);
        Assert.That(r, Is.LessThan(60));
        Assert.That(b, Is.LessThan(70));
        // Кольцо — оранжевый акцент темы (FD7442): красный ярче зелёного, зелёный ярче синего.
        r = g = b = 0;
        AbilityIconRules.Paint(ref r, ref g, ref b, u + AbilityIconRules.MarkRadius * .8f, v, pixel);
        Assert.That(r, Is.EqualTo(0xFD).Within(2));
        Assert.That(g, Is.EqualTo(0x74).Within(2));
        Assert.That(b, Is.EqualTo(0x42).Within(2));
        // Огонёк — светлее к середине.
        r = g = b = 0;
        AbilityIconRules.Paint(ref r, ref g, ref b, u, v, pixel);
        Assert.That(r, Is.GreaterThan(240));
        Assert.That(g, Is.GreaterThan(0x74));
    }
}
