# Иконки форм Шквала — ChatGPT

Так же, как делали иконки Вихря (`ART/UI/icons-whirlwind-forms-2026-10-02`): владелец генерит, мы готовим вложения, промпты и перекраску.

## Как пользоваться
1. Новый чат на каждую иконку. Приложи 4 картинки по порядку: `1-icon-style-sheet` → `2-icon-squall-current` → `3-whirlwind-form-icons` → `4-squall-form-motifs` (номер Image в промпте = порядок вложения).
2. Вставь промпт целиком (одна строка, `prompts.txt` рядом с этой папкой: строка 1 — Охота, 2 — Пенный след, 3 — Неуловимый), отправь, потом «Повторить» — 2 варианта.
3. Лучший скачай в PNG и пришли, подписав, какая это иконка. В игру они встанут под именами `Icon_Squall_Hunt`, `Icon_Squall_FoamTrail`, `Icon_Squall_Elusive` (Resources/UI/Abilities) — HUD, карточки наград и подсказки подхватят их сами; до этого у форм базовая иконка с меткой формы.

## Вложения
| # | Файл | Что это |
|---|------|---------|
| 1 | `1-icon-style-sheet.jpg` | Наши иконки способностей (Вихрь, Шквал, Рассекающий, Крушение, Рывок, Склянка) — эталон стиля, тот же лист, что для Вихря |
| 2 | `2-icon-squall-current.jpg` | Нынешняя иконка Шквала в игре (`Icon_Squall.png`): сабля по диагонали, зигзаг прыжков через три точки удара — композиция, которую держим |
| 3 | `3-whirlwind-form-icons.jpg` | Принятые иконки Вихря в пене: база, Буря, Водоворот, Пенные волны — как рисуем воду и пену и как форма отличается от базы |
| 4 | `4-squall-form-motifs.jpg` | Кадры форм Шквала, выбранные владельцем: Hunt (прыжки и всплески на целях — Охота делается на их основе), Foam Trail (`trail-wavy-1`), Elusive (`elusive-return-3`) |

Цвет в промптах — бирюза с белой пеной, как у базы: ChatGPT у Вихря всё равно рисовал одну бирюзу, а разводили формы по цветам скриптом `recolor_forms.py` (только насыщенная вода; пена, сабля, фон не трогаются). Без эмблем и знаков (правило набора), без текста и рамки.

---

## 1. Охота — `Icon_Squall_Hunt`
```
Create a square 1:1 game ability icon in exactly the same painted style, dark navy background and framing as the icons in Image 1 and Image 3: a bold centred composition, painted strokes, a clear silhouette readable at 64 pixels, no frame, no border, no text, no emblem. Keep the composition and the sabre of Image 2 (our Squall icon: a curved sabre with a bronze guard on the diagonal, wrapped by a zig-zag chain of fast leaps between three impact points), but paint every leap as turquoise sea water with chunky white foam crests, a thin dark outline and small cut-out foam droplets, exactly like the water in the Whirlwind icons of Image 3, instead of the icy blue light and crystals. Motif from the first panel of Image 4 ("Hunt"): the chain of leaps closes in on one battered, cracked prey point that bursts in a big white foam kill splash, and from that splash one short extra leap springs onward as a curling foam wave with a sharp arrow-like crest, so it reads as a hunt where every kill grants another leap. Colours: turquoise water and white foam on the dark navy background like Image 3, no orange or red. Square, centred, fills about 80% of the canvas, no logos, no letters, no emblems.
```

## 2. Пенный след — `Icon_Squall_FoamTrail`
```
Create a square 1:1 game ability icon in exactly the same painted style, dark navy background and framing as the icons in Image 1 and Image 3: a bold centred composition, painted strokes, a clear silhouette readable at 64 pixels, no frame, no border, no text, no emblem. Keep the composition and the sabre of Image 2 (our Squall icon: a curved sabre with a bronze guard on the diagonal, wrapped by a zig-zag chain of fast leaps between three impact points), but paint every leap as turquoise sea water with chunky white foam crests, a thin dark outline and small cut-out foam droplets, exactly like the water in the Whirlwind icons of Image 3, instead of the icy blue light and crystals. Motif from the second panel of Image 4 ("Foam Trail"): the zig-zag leaps lie flat as three broad strips of turquoise water with foamy white edges and simple flow lines, each strip ending in a white foam splash at an impact point, with foam bubbles churning along the strips so they read as a lingering foam trail that slows enemies, and the sabre slashing across the last splash. Colours: turquoise water and white foam on the dark navy background like Image 3, no orange or red. Square, centred, fills about 80% of the canvas, no logos, no letters, no emblems.
```

## 3. Неуловимый — `Icon_Squall_Elusive`
```
Create a square 1:1 game ability icon in exactly the same painted style, dark navy background and framing as the icons in Image 1 and Image 3: a bold centred composition, painted strokes, a clear silhouette readable at 64 pixels, no frame, no border, no text, no emblem. Keep the composition and the sabre of Image 2 (our Squall icon: a curved sabre with a bronze guard on the diagonal, wrapped by a zig-zag chain of fast leaps between three impact points), but paint every leap as turquoise sea water with chunky white foam crests, a thin dark outline and small cut-out foam droplets, exactly like the water in the Whirlwind icons of Image 3, instead of the icy blue light and crystals. Motif from the third panel of Image 4 ("Elusive"): a see-through afterimage silhouette made of water and foam stands at the starting point, thin fast foam streaks zig-zag out through two white foam hit splashes, and one long smooth foam arc curves back to the afterimage with the sabre riding at its head, so it reads as striking untouched and returning to the start. Colours: turquoise water and white foam on the dark navy background like Image 3, no orange or red. Square, centred, fills about 80% of the canvas, no logos, no letters, no emblems.
```

## 0. По желанию — базовый Шквал в пене (`Icon_Squall`)
Нынешняя иконка Шквала — ледяной голубой свет с кристаллами, а Шквал в игре переходит в «морскую пену» (как Вихрь, у которого базу тоже перерисовали). Если формы выйдут в пене, а база останется ледяной, ряд будет разнобойным. Решает владелец.
```
Create a square 1:1 game ability icon in exactly the same painted style, dark navy background and framing as the icons in Image 1 and Image 3: a bold centred composition, painted strokes, a clear silhouette readable at 64 pixels, no frame, no border, no text, no emblem. Keep the composition and the sabre of Image 2 exactly (a curved sabre with a bronze guard on the diagonal, wrapped by a zig-zag chain of fast leaps between three impact points), but replace the icy blue light, the star bursts and the ice crystals with turquoise sea water with chunky white foam crests, a thin dark outline, white foam splashes at the three impact points and small cut-out foam droplets, like the first Whirlwind icon in Image 3. Colours: turquoise water and white foam on the dark navy background, no orange or red. Square, centred, fills about 80% of the canvas, no logos, no letters, no emblems.
```

---

## Если вышло не так (дописать в тот же чат)
- Не наш стиль (фотореализм, 3D, другая подложка): `Match the painted style and the dark navy background of the icons in Image 1 and Image 3 exactly.`
- Сабля другая: `Keep the sabre exactly like in Image 2: curved blade, bronze guard, the same angle on the diagonal.`
- Осталась ледяная голубизна с кристаллами: `No ice and no crystals: turquoise sea water and white foam like Image 3.`
- Плохо читается мелко: `Simplify: fewer strokes, one bold shape, stronger contrast, readable at 64 pixels.`
- Появились рамка, текст или знак: `No frame, no border, no circle outline, no text, no emblem — just the motif on the dark navy background.`

## Дальше (делаем мы)
1. Присланные PNG → `chatgpt-results/squall-hunt.png`, `squall-foamtrail.png`, `squall-elusive.png` (и `squall-base.png`, если делали 0).
2. `python recolor_forms.py` → `final/` и `sheet-final.jpg` (цвета форм — по решению владельца, см. PALETTE в скрипте; `--keep` — без перекраски).
3. В игру: `final/*.png` → `razlom/Assets/Resources/UI/Abilities/Icon_Squall_Hunt.png` и т. д.; `.meta` — копия `Icon_Squall.png.meta` с новым GUID (как у форм Вихря). Имена файлов совпадают с `AbilityIconRules.FormSuffix` (патч f8/05).
4. Папку переносим в `ART/UI/icons-squall-forms-2026-10-02/` (сейчас она НЕ в ART, только в artifacts).
