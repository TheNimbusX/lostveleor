# Lost Veleor — как запустить

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

Управление: ПКМ по земле — идти, ПКМ по врагу — атаковать, `1` — способность, `L` — выйти из Разлома, `R` — повторить забег, `C` — вернуться в лагерь, `E` — войти в Разлом.
