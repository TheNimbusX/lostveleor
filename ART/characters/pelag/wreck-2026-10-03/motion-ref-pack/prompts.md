# Крушение: позы и стартовый кадр (ChatGPT), v2 — якорь на цепи

Пак — реф движения тела, эффектов в нём нет. Воду, вал, горб Девятого вала и панцирь строим в игре по целевым кадрам (`../chatgpt-results/`: A-base, B-breakwater-seagreen, C-ninth-wave, D-shell).

**Главное (владелец 03.10): «якорь должен быть на цепи и должен быть физичный».** Пелаг держит рукоять цепи обеими руками, якорь летит на цепи длиной примерно 1,5–2 м, как кистень: у него вес, инерция, дуга, натяг цепи в ударе и провис в паузе. За веретено якорь никто не держит. Прежний вариант «якорь как молот» отвергнут (`prompts-hammer-rejected.md`).

Механика: серия из трёх тяжёлых ударов якорем на цепи — мах справа налево, обратный мах слева направо, третий — якорь через голову по вертикальной дуге и оземь перед собой по полосе. Формы: Волнорез, Девятый вал (удержание до 1 с — якорь раскручивается над головой, отпуск — удар вдвое сильнее), Водяной панцирь (серию не сбить). Вкус владельца: резко, быстро, тело живое.

Сейчас в игре (код, 03.10): Крушение уже бьёт якорем на цепи 0,9–2 м (`PelagAnchorSlamView.Wreck.cs`, плюс 0,42 м на кольцо). Рукоять цепи в левой руке (`ChainGripPosition`), от неё основная цепь идёт к якорю; правая (`ChainSupportPosition`) держит цепь рядом как опору: до левой перемычка 0,5–1,1 м, от пояса к правой идёт хвост. Касание на ~2,25 м перед героем, в махах на высоте ~0,7 м (бедро), в третьем ударе 0,28 м. Сабля за кушаком у левого бедра (`SetSlamOwnership` → `BeginAnchorUse`). Расхождение для сборки: в `PoseWreck` первый мах идёт слева направо, это зеркально листу и Sim («справа, обратный слева»). Лист верен, переворачивать надо постановку. Третий удар код сейчас поднимает над головой и чуть вперёд; в листе дуга идёт из-за спины, так физичнее.

## Порядок
1. **B: лист ключевых поз (база, позы 1–7).** Новый чат, приложить по порядку `1`, `2`, `3`, `4`, `5B`. Вставить промпт **B**. 1–2 повтора, лучший сохранить как `B-key-poses-chatgpt.png`. **Уже сделан 03.10 14:29** (один ракурс с подписями; 2, 4, 5, 6, 7 годятся). Целиком не перегенерировать: в том же чате отправить строки C «Поза 1, якорь у руки» и «Поза 3, два якоря» (по одной, после каждой проверить, что остальные позы не уехали), удачный вариант сохранить как `B-key-poses-chatgpt-fix13.png` (старый не затирать).
2. **B2: позы форм (8–11).** Новый чат, приложить `1`, `2`, `3`, `4` и пятым свой лист B. Промпт **B2**, сохранить как `B2-form-poses-chatgpt.png`.
3. **S: стартовый кадр в игровой камере** — только если появятся кредиты на видео-реф (Higgsfield сейчас пуст); для клипов не нужен. Новый чат, приложить `1`, `2`, `3`, `4`, `5S` и шестым свой лист B. Промпт **S**, сохранить как `S-start-frame-chatgpt.png` (16:9).
4. Если вышло не так — строку из раздела **C** в тот же чат.
5. Прислать правку B (позы 1 и 3) и B2; S — только под видео. Клипы тела собираю в Blender по листам, физику якоря и цепи — по разбору `artifacts\wreck\anchor-tech` (`DESIGN.md`, когда будет готов).

## Позы (номера держим до клипов)
| # | Поза | Что важно |
|---|------|-----------|
| 1 | Готовность | Обе руки на рукояти цепи у правого бедра, якорь висит/качается сзади-справа на цепи, корпус скручен вправо, вес на задней правой |
| 2 | Мах 1, справа налево | Руки тянут цепь поперёк тела, якорь на натянутой цепи летит плоско на уровне бедра впереди-слева, ~2 м от тела; корпус и таз доворачиваются влево |
| 3 | Связка | Инерция уносит якорь за спину влево, цепь хлещет дугой, руки у левого бедра, корпус скручен влево, короткий шаг правой вперёд |
| 4 | Мах 2, обратный слева направо | Якорь на натянутой цепи плоско на уровне бедра впереди-справа, корпус и таз вправо, вес на передней правой |
| 5 | Через голову | Руки вверху, якорь на цепи идёт по вертикальной дуге над головой и сзади, цепь натянута вверх-назад, грудь открыта, без прыжка |
| 6 | Удар оземь | Руки рывком вниз-вперёд, якорь на конце натянутой цепи врезался в землю ~2 м впереди, глубокий выпад, спина прямая |
| 7 | Восстановление | Подтянул цепь: якорь доволочился до передней ноги, цепь провисла, широкая устойчивая стойка |
| 8 | Девятый вал: удержание | Раскручивает якорь над головой по горизонтальному кругу на цепи («вертолёт»), руки вверху, очень низкая широкая стойка |
| 9 | Девятый вал: отпуск | Как 6, но сильнее: цепь вытянута на всю длину, якорь глубже в земле, выпад шире |
| 10 | Водяной панцирь: упор | Мах 2 в миг касания, но врос в землю: стойка шире и ниже, стопы плашмя, подбородок вниз |
| 11 | Волнорез: протяжка | После удара оземь шагает вперёд за якорем, руки вперёд-вниз на натянутой цепи, якорь по инерции пашет землю впереди, наклон ~40° |

## Как выбрать вариант
- Пелаг наш: лицо, повязка, одежда. Сабля всё время за кушаком у левого бедра.
- Якорь наш (Image 3 и 4), размером примерно в половину роста Пелага, на тёмной цепи длиной примерно с рост Пелага. Левая рука на кожаной рукояти с красной кисточкой, от неё цепь к якорю; правая на цепи рядом (как в коде: левая `ChainGripPosition`, правая `ChainSupportPosition`).
- Якорь ВСЕГДА на цепи на расстоянии от рук, никогда не в руках за веретено.
- Видна физика: в махах цепь натянута прямой линией, в паузах провисает; якорь отстаёт от рук по инерции.
- Ступни на земле, без прыжков и сальто, корпус наклонён не больше ~45°, спина прямая, грудь и таз поворачиваются вместе.

## Вложения
| # | Файл | Что это | Где нужен |
|---|------|---------|-----------|
| 1 | `1-pelag-model-4views.jpg` | Модель Пелага в 4 ракурсах | B, B2, S |
| 2 | `2-pelag-ingame.jpg` | Цвет и яркость Пелага в игре | B, B2, S |
| 3 | `3-anchor-on-back-ingame.jpg` | Наш якорь в игре (в части кадров сабля в руке, в промптах сказано это игнорировать) | B, B2, S |
| 4 | `4-anchor-chain-design.jpg` | Лист дизайна якоря, цепи и рукояти | B, B2, S |
| 5B | `5B-abordage-keyposes-format.png` | Лист поз Абордажа — ТОЛЬКО раскладка и рендер (в нём 6 колонок, у нас 7) | B |
| 5S | `5S-scene-game-camera.jpg` | Кадр игры: камера, свет, арена | S |
| 6 | свой `B-key-poses-chatgpt.png` | Лист поз из шага 1 | B2 (пятым), S (шестым) |

---

## B. Лист ключевых поз, база 1–7
Вложения: 1, 2, 3, 4, 5B.

```
This is a CHARACTER ANIMATION KEY-POSE SHEET on a plain light grey background, like Image 5 — NOT a game screenshot, NOT a VFX or skill frame, no arena, no enemies, no water. Using Image 1 (our game character Pelag in four views), Image 2 (the same character in a real gameplay frame, for his in-game colours), Image 3 (in-game close-ups of his dark iron anchor; ignore the sabre drawn in some of its frames) and Image 4 (the design sheet of that anchor, its chain and the leather-wrapped chain grip with a red tassel; ignore its glow and impact effects), create an animation key-pose sheet for his skill Wreck: a fast series of three heavy blows with the anchor swung ON ITS CHAIN like a flail. Use Image 5 only as the layout and render reference: the same 16:9 landscape format, plain light grey background, thin ground line under each row, two rows of seven full-body figures (Image 5 has six; draw seven) of the same size with clear empty space between them, the top row in a strict side view with Pelag facing right and the bottom row showing exactly the same poses from a three-quarter front view, small numbers 1 to 7 above the columns and no other text; do not copy the poses of Image 5. In every pose both his hands hold only the chain, never the anchor itself: his left fist is closed on the leather-wrapped grip, his right fist holds the chain right next to it, and the heavy anchor (about half his height) hangs or flies at the end of a dark iron chain about as long as he is tall that runs out from his left fist; it behaves physically — in the swings the chain is pulled taut in a straight line and the anchor trails behind the hands by its own weight and momentum, in the pauses the chain sags; the curved sabre stays tucked in his red sash at his left hip exactly as in Image 1 and he never holds it. Pose 1, ready: both hands on the chain grip at his right hip, the anchor hanging and swinging low behind him to the right on the chain, weight on the back right foot, the left foot forward, knees bent, torso coiled to the right. Pose 2, first swing at contact, from his right to his left: both arms pull the chain across his body, the chain taut and straight, the anchor flying flat at hip height about two metres ahead and to the left, hips and chest turned together to the left, weight on the planted front left foot, the back right heel lifted. Pose 3, link: the anchor's momentum carries it on around behind him to the left, the chain whipping in an arc, both hands at his left hip, the torso coiled to the left, the right foot taking a short step forward. Pose 4, second swing at contact, a backhand from his left to his right: the chain taut and straight, the anchor flying flat at hip height about two metres ahead and to the right, hips and chest turned together to the right, weight on the planted front right foot, the back left heel lifted. Pose 5, overhead: both arms raised high, the anchor travelling on the taut chain in a vertical arc high above and behind his head, the chest open and upright, weight on the back foot, the left foot stepped forward, no jump. Pose 6, the slam: both arms yanked down and forward, the chain taut and straight from his hands to the anchor, which has crashed into the ground about two metres in front of him, a deep forward lunge on the flat front left foot, the back straight and leaning forward about 40 degrees, the head up looking along the ground. Pose 7, recovery: he has pulled the chain back, the anchor has dragged along the ground and now rests near his front foot, the chain slack and sagging, a wide balanced ready stance, knees bent. The body must stay solid and athletic in every pose: the spine long and straight, never curled into a ball, chest and hips turned together, the head following the chest, knees and elbows bending only the natural way, the weight clearly on the feet, both feet on the ground in every pose. Pelag must match Image 1 exactly in every figure: tanned young islander, dark messy hair, red headband, white short-sleeve wrap shirt with small turquoise trim, red sash, wide cream knee-length trousers with red and turquoise cuffs, brown wrist wraps, brown sandals; the anchor, chain and grip match Images 3 and 4 exactly. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects, no water, no foam, no splashes, no cracks, no motion lines, no enemies, no logos, no UI.
```

## B2. Позы форм 8–11
Вложения: 1, 2, 3, 4 и пятым свой лист B.

```
This is a CHARACTER ANIMATION KEY-POSE SHEET on a plain light grey background, like Image 5 — NOT a game screenshot, NOT a VFX or skill frame, no arena, no enemies, no water. Using Image 1 (our game character Pelag in four views), Image 2 (the same character in a real gameplay frame, for his in-game colours), Image 3 and Image 4 (his dark iron anchor, its chain and the leather-wrapped chain grip with a red tassel; ignore the sabre drawn in some frames of Image 3 and the effects in Image 4) and Image 5 (his Wreck key-pose sheet, poses 1 to 7), create a second key-pose sheet for the special forms of Wreck with exactly the same character render, colours, anchor and chain as Image 5 (ignore its titles, captions, logo and dust; follow the layout described here, not the layout of Image 5): a 16:9 landscape image on a plain light grey background with a thin ground line under each row, two rows of four full-body figures of the same size with clear empty space between them, the top row in a strict side view facing right and the bottom row the same poses from a three-quarter front view, small numbers 8 to 11 above the columns and no other text. As in Image 5, his left fist holds the leather-wrapped grip, his right fist holds the chain right next to it, and the heavy anchor flies or hangs at the end of the chain, physically, never held by its shank; the sabre stays tucked in his red sash at his left hip. Pose 8, Ninth Wave hold: he whirls the anchor around above his head in a wide horizontal circle on the taut chain to build up power, both arms raised, a very low wide stance with the weight sunk deep, knees deeply bent, eyes on the ground ahead, both feet flat. Pose 9, Ninth Wave release: a bigger and deeper version of the slam: the chain stretched to its full length, taut and straight, the anchor buried in the ground far in front of him, a very wide lunge with the back knee almost touching the ground, the head up. Pose 10, Water Shell braced swing: the backhand swing at contact but rooted like a rock: a wider and lower stance, both feet flat, knees deeply bent, shoulders hunched a little forward, chin down, the chain taut and the anchor flying flat at hip height ahead and to the right. Pose 11, Breakwater drag: right after the slam he steps through after the anchor, both arms reaching forward and down on the taut chain, the anchor ploughing forward along the ground ahead of him under its own momentum, the chest leaning forward about 40 degrees, the head up looking straight ahead. The body must stay solid and athletic in every pose. Pelag must match Image 1 exactly; the anchor, chain and grip match Images 3 and 4 exactly. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects, no water, no foam, no waves, no shell, no cracks, no motion lines, no enemies, no logos, no UI.
```

## S. Стартовый кадр серии, игровая камера
Вложения: 1, 2, 3, 4, 5S и шестым свой лист B.

```
Using Image 1 (our game character Pelag in four views), Image 2 (the same character in a real gameplay frame, for his in-game colours), Image 3 and Image 4 (his dark iron anchor, its chain and the leather-wrapped chain grip with a red tassel; ignore the sabre drawn in some frames of Image 3 and the effects in Image 4), Image 5 (a real screenshot of our game) and Image 6 (his Wreck key-pose sheet), create one 16:9 landscape game screenshot: the instant just before Pelag's first anchor swing. Use exactly the camera angle, lighting, ground and bright painted 3D game look of Image 5, framed about 1.5 times closer so his pose reads clearly. Pelag stands on open bare ground a little left of and below the centre; three forest guardian enemies exactly like those in Image 5 stand up and to the right of him, facing him, the nearest about two and a half metres away. Pelag is in pose 1 of Image 6 (where Image 6 differs, follow this text): both hands on the chain grip at his right hip, the heavy anchor hanging low behind him to the right at the end of its chain, ready to swing, the curved sabre tucked in his red sash at his left hip. Pelag must match Image 1 exactly; the anchor, chain and grip match Images 3 and 4 exactly. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects at all: no water, no foam, no splashes, no cracks, no trails, no motion lines, no glow, no text, no UI.
```

---

## C. Если вышло не так (строку отправить в тот же чат)
- Прислал кадры игры с эффектами вместо листа поз: начать НОВЫЙ чат (не тот, где делали целевые кадры) и приложить только 1, 2, 3, 4, 5B; или отправить: `Wrong output: I need a character animation key-pose sheet on a plain light grey background exactly in the layout of Image 5 — two rows of figures, no arena, no enemies, no water, no effects.`
- Якорь в руках за веретено: `He never holds the anchor itself: both hands hold only the leather chain grip and the chain, and the anchor flies or hangs at the end of a chain about as long as he is tall.`
- Руки не так: `His left fist is closed on the leather-wrapped grip with the chain running from it out to the anchor; his right fist holds the chain right next to the left one.`
- Цепь короткая или якорь у руки: `The chain is about as long as he is; at contact the anchor is about two metres away from his hands on a taut straight chain.`
- Нет веса, цепь висит как верёвка в махе: `In the swings the chain is pulled taut in a straight line and the anchor trails behind the hands by its own weight; only in the pauses does the chain sag.`
- Персонаж уплыл: `Every figure must be the same character as Image 1: same face, red headband, white wrap shirt, red sash, cream trousers, sandals, and the curved sabre tucked in his sash at his left hip.`
- Сабля в руке: `He never holds the sabre in this skill: it stays tucked in his red sash at his left hip in every figure.`
- Якорь не наш: `The anchor must be exactly the one in Images 3 and 4: dark blue-grey forged iron, long shank, two broad hooked crescent arms, faceted diamond at the bottom, ring on top, dark iron chain, leather-wrapped grip with a red tassel.`
- Прыгает: `Both feet stay on the ground in every pose; no jumps, no flips, no spins.`
- Фигуры слиплись или обрезаны: `Keep all figures full body, the same size, with clear empty space between them; the chain and anchor never cross into the next figure; nothing overlaps or is cut off.`
- Ракурсы перепутаны: `Top row strictly side view, facing right; bottom row the same poses from a three-quarter front view.`
- Лишний текст: `Only the small pose numbers above the columns, no other text: no titles, no captions, no logos.`
- Два якоря в одной фигуре: `There is only ONE anchor in each figure, at the far end of the chain; the near end of the chain is only the leather grip with the red tassel in his left fist.`
- Поза 1, якорь у руки (лист B 03.10): `Redraw only pose 1, keep the other poses exactly: the anchor is not lifted near his hands or shoulder; it hangs low behind him to the right at the far end of a chain as long as he is tall, almost touching the ground, both fists on the grip at his right hip.`
- Поза 3, два якоря (лист B 03.10): `Redraw only pose 3, keep the other poses exactly: one anchor only, carried by its momentum around behind him to his left on the whipping chain, both fists at his left hip, nothing above his head.`
