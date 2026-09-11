# The War Remains — как запустить

> **Агентам и разработчикам:** документация проекта живёт в `AGENTS/`.
> Начинать с [`AGENTS/README.md`](AGENTS/README.md) — там правила, устройство
> проекта и как проверять работу. Текущее состояние — [`AGENTS/STATE.md`](AGENTS/STATE.md).
> Новых `.md` в проекте не заводить.

Нужны Windows, [Git LFS](https://git-lfs.com/) и Unity Hub с редактором **Unity 6000.5.10f1**.

```powershell
git lfs install
git clone https://github.com/TheNimbusX/lostveleor.git
cd lostveleor
git lfs pull
```

1. В Unity Hub нажми **Add → Add project from disk** и выбери папку `razlom`.
2. Открой сцену `Assets/Scenes/SampleScene.unity`.
3. Нажми **Play** — в редакторе боевой прототип запустится автоматически.

Управление: ПКМ по земле — идти, ПКМ по врагу — атаковать. Способности —
`Q` `W` `E` `R` `F` либо `1` `2` `3` `4` `5`; пятый слот — кувырок. У прицельных ЛКМ подтверждает, ПКМ отменяет.
Забег: `E` — войти в Разлом, `L` — выйти, `R` — повторить, `C` — вернуться в лагерь.
