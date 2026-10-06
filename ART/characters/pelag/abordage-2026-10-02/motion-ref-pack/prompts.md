# Абордаж: позы и стартовый кадр (ChatGPT)

Пак нужен для рефа движения тела, эффекты в него не входят. Пену, всплеск на зацепе и след строим в игре по целевому кадру A (`6S-abordage-target-A.png`, его прислал ChatGPT 02.10 в 21:48).

Механика: Пелаг коротко, по-гарпунному, бросает якорь правой рукой. Якорь летит во врага (не в точку на земле) и впивается в него. Цепь натягивается, и Пелага резко тянет к врагу низко над землёй. На прилёте он бьёт свободным правым кулаком.
Руки (как в игре): сабля во время навыков с якорем убрана за кушак у левого бедра (`PelagEquipmentView.BeginAnchorUse`), рукоять цепи в левой руке (`ChainGripPosition`). Свободна правая рука: ею бросают якорь и бьют кулаком.
Владелец 01.10 сказал о старом Абордаже: «очень долго замах цепи… супер медленно». Поэтому замах здесь короткий: без раскрутки цепи над головой, бросок как у гарпунёра.

## Порядок
1. **B: лист ключевых поз (база, позы 1–6).** Новый чат, приложить по порядку `1`, `2`, `3`, `4`, `5B`. Вставить промпт **B**. Сделать 1–2 повтора и сохранить лучший как `B-key-poses-chatgpt.png`.
   **Готово 22:10** (`B-key-poses-chatgpt.png`). Что видно: сабля за кушаком, цепь в позах 2–4 прямая, якорь наш. Отклонения:
   - **Руки в нижнем ряду (3/4).** Похоже, в позах 2 и 5 они поменялись: бросок и кулак левой, рукоять в правой. В позах 1, 3, 4 и 6 руки по брифу. Сбоку руку не различить. В клипе беру руки по брифу. Если лист нужен чистым, в тот же чат отправить строку «Не та рука бьёт» из раздела C.
   - **Поза 4 высоко.** Стопы примерно на 20 % роста над землёй, колени поджаты, читается прыжок. В клипе опускаю до 3–9 % роста. Исправить лист: строка «Высоко летит».
   - **Цепь позы 3 заходит в фигуру 4** в обоих рядах. Легко принять её за вторую цепь позы 4.
   - **Поза 1:** якорь над головой, выше брифа. При замахе в 2 тика это 1–2 кадра, оставляю.
2. **B2: позы форм (7–10).** Новый чат, приложить `1`, `2`, `3`, `4` и пятым свой лист B. Вставить промпт **B2** и сохранить как `B2-form-poses-chatgpt.png`.
3. **S: стартовый кадр в игровой камере, по желанию.** S — первый кадр для видео-рефа, а видео сейчас не будет (Higgsfield пуст). Поэтому S нужен только как проверка, читается ли поза 1 в игровой камере, или на случай, если кредиты появятся. Сначала B2. Новый чат, приложить `1`, `2`, `3`, `4`, `5S`, `6S` и седьмым свой лист B. Вставить промпт **S** и сохранить как `S-start-frame-chatgpt.png` (16:9).
4. **S2: запасной студийный кадр, по желанию (тоже только под видео).** Новый чат, приложить `1`, `2`, `3`, `4` и пятым свой лист B. Вставить промпт **S2**. Он нужен, если видео из игровой камеры выйдет нечитаемым. Урок рывка: при камере сверху тело мелкое, и реф не годится.
5. Если вышло не так, отправить в тот же чат строку из раздела **C**.
6. Прислать мне B, B2 и S (и S2, если делал). Клипы собираю в Blender по листам поз, как у Шквала v2 (кредиты Higgsfield кончились, видео-рефа не будет).

Номер Image в промпте совпадает с порядком вложений. Цифра в начале имени файла и есть его номер.

## Позы (номера держим до клипов)
| # | Поза | Что важно |
|---|------|-----------|
| 1 | Замах | Короткий и сжатый. Якорь в правой руке отведён за правое плечо, левый кулак с рукоятью цепи впереди, вес на задней правой ноге |
| 2 | Бросок | Правая рука выброшена к цели, якорь только что сорвался с руки, цепь прямая. Вес на передней левой ноге |
| 3 | Рывок цепью | Цепь натянута, Пелага сдёрнуло с места. Тело одной длинной прямой от пятки до левого кулака, левая рука вытянута по цепи, правый кулак у рёбер. Низко, ступни у самой земли |
| 4 | Середина протяжки | Ноги подбираются под приземление, левый локоть сгибается («подтягивается»), правый кулак взведён далеко назад |
| 5 | Прилёт и кулак | Левая стопа встала плашмя в выпаде, прямой правой в грудь цели, плечи и таз доворачиваются вместе |
| 6 | Восстановление | Широкая устойчивая стойка, кулаки в защите, якорь висит на короткой цепи у левой ноги |
| 7 | Гейзер | Апперкот снизу вверх: из низкого приседа на прилёте тело разгибается вверх, правый кулак уходит над головой, вес на носках обеих ног |
| 8 | Пробоина | Прямой «насквозь»: глубокий длинный выпад, правая рука выпрямлена до конца, плечо и корпус вложены в удар, левая стопа плашмя |
| 9 | Обвал: подскок | Короткий подскок в конце протяжки, правый кулак поднят над головой |
| 10 | Обвал: удар оземь | Жёсткое приземление в глубокий присед, правый кулак вбит в землю |

У Метки своей позы нет: это база (позы 1–6), метка рисуется эффектом. Цвета форм в паке не нужны, потому что эффектов в нём нет. Для справки (02.10 ~22:35): база — бирюза, Обвал — кобальт #2D5BE3, Гейзер — морская зелень #1FB37E, Пробоина — маджента #D23C9C. Таран и Перецеп отвергнуты (повторяют «На вылет» и Шквал), Метка ушла в таланты. Красного, оранжевого и золотого нет: это цвета телеграфов врагов.

## Как выбрать вариант
- Пелаг наш: лицо, повязка, одежда. Сабля всё время за кушаком у левого бедра, в руках её нет.
- Якорь наш (как на Image 3 и 4): длинное веретено, два загнутых рога, гранёный ромб снизу, кольцо сверху. Цепь тёмная, рукоять обмотана кожей и с красной кисточкой.
- Замах короткий, без раскрутки цепи. По позе 1 видно, что бросок будет мгновенным.
- В позах 3–4 цепь прямая и натянутая, тело длинное и вытянутое, не свёрнуто в комок. Сверху комок не читается, а якоря на спине в эти моменты нет.
- Ступни у земли, без высоких прыжков и сальто. Корпус наклонён не больше чем на ~45°.
- Корпус цельный: спина прямая, грудь и таз поворачиваются вместе, голова следует за грудью, локти и колени гнутся только естественно.
- В позе 5 передняя стопа стоит плашмя, кулак идёт из инерции протяжки.

## Вложения
| # | Файл | Что это | Где нужен |
|---|------|---------|-----------|
| 1 | `1-pelag-model-4views.jpg` | Модель Пелага в 4 ракурсах на светлом фоне. Сабля за кушаком, якорь на спине | B, B2, S, S2 |
| 2 | `2-pelag-ingame.jpg` | Чистый кадр игры: цвет и яркость Пелага в игре (сабля здесь в руке, это только про цвет) | B, B2, S, S2 |
| 3 | `3-anchor-on-back-ingame.jpg` | Наш якорь на спине в игре: 9 + 6 крупных вырезов, кольцо и цепь у плеча | B, B2, S, S2 |
| 4 | `4-anchor-chain-design.jpg` | Лист дизайна якоря: голова, цепь, рукоять с красной кисточкой. Свечение и взрыв на нём не брать | B, B2, S, S2 |
| 5B | `5B-squall-keyposes-format.png` | Принятый лист поз Шквала: только раскладка и рендер, позы оттуда не брать | B |
| 5S | `5S-scene-game-camera.jpg` | Кадр игры: камера, свет, масштаб, лесная арена, хранители леса | S |
| 6S | `6S-abordage-target-A.png` | Целевой кадр A от ChatGPT: та же арена и та же цель, Пелага тянет по цепи | S |
| 7 | свой `B-key-poses-chatgpt.png` | Лист поз из шага 1: поза 1 для стартового кадра | S (седьмым), B2 и S2 (пятым) |

---

## B. Лист ключевых поз, база 1–6 (ChatGPT)
Вложения: 1, 2, 3, 4, 5B.

```
Using Image 1 (our game character Pelag in four views), Image 2 (the same character in a real gameplay frame, for his in-game colours), Image 3 (in-game close-ups of his dark iron anchor strapped on his back) and Image 4 (the design sheet of that anchor, its chain and the leather-wrapped chain grip with a red tassel), create an animation key-pose sheet for his skill Abordage: he hurls his anchor on its chain at an enemy, the anchor hooks the enemy, the chain snaps taut and yanks him to the enemy low and very fast, and he finishes with a punch of his free right fist. Use Image 5 only as the layout and render reference: the same 16:9 landscape format, plain light grey background, thin ground line under each row, two rows of six full-body figures of the same size with clear empty space between them, the top row in a strict side view with Pelag moving toward the right and the bottom row showing exactly the same poses from a three-quarter front view, small numbers 1 to 6 above the columns and no other text; do not copy the poses of Image 5 and do not put the sabre in his hand as in Image 5. In every pose the curved sabre stays tucked in his red sash at his left hip exactly as in Image 1 and he never holds it; the chain grip is always in his left fist; his right hand is the throwing hand and the punching fist. Pose 1, anticipation, short and compact: weight on the back right foot, left foot forward pointing at the target, knees bent, torso coiled to the right, the right hand gripping the anchor shank with the anchor cocked back just behind his right shoulder like a harpoon thrower, the left fist forward at chest height holding the chain grip, a short loop of chain hanging between his hands, eyes on the target; no big wind-up and no chain twirling. Pose 2, release: the right arm whipped fully forward toward the target, the anchor just leaving his right hand a little ahead of him with the chain trailing in a straight line back to his left fist, weight shifted onto the planted front left foot, the back right leg extended with the heel lifted, hips and chest turned square to the target together, the left fist pulled in to the left hip. Pose 3, yanked by the chain: the chain is dead straight and taut and runs from his left fist forward out of his column, he is jerked forward off his feet, his body one long straight diagonal from the trailing right heel to the left fist at about 40 degrees, the left arm fully extended forward along the chain, the left shoulder leading, the head low behind the left arm looking at the target, the front toes just skimming the floor, the right fist pulled back to his right ribs. Pose 4, mid-pull: flying low with both feet only a hand's height above the floor, the left knee tucked forward and the right leg trailing, the chain still taut and straight, the left elbow bending as he reels himself in, the right fist cocked far back at shoulder height loading a punch, the right shoulder turned back, the torso leaning forward but straight, the head level and the eyes on the target. Pose 5, arrival punch: the left foot planted flat in a deep forward lunge, the right leg extended behind with the heel lifted, a full straight right punch driving forward at chest height, the right shoulder rotated forward, hips and chest turned together into the punch, the left fist pulled back to the left hip still holding the chain grip with the chain now slack, the head behind the fist. Pose 6, recovery: one short step back into a wide balanced ready stance, knees bent, hips low, chest up, both fists up in a loose guard, the anchor hanging low beside his left leg on a short slack chain from the grip in his left fist, settled. The body must stay solid and athletic in every pose: the spine long and straight, never curled into a ball and never folded more than about 45 degrees forward, chest and hips turned together, the head following the chest, knees and elbows bending only the natural way, the weight clearly on the feet. Pelag must match Image 1 exactly in every figure: tanned young islander, dark messy hair, red headband, white short-sleeve wrap shirt with small turquoise trim, red sash, wide cream knee-length trousers with red and turquoise cuffs, brown wrist wraps, brown sandals; the anchor and chain match Images 3 and 4 exactly: dark blue-grey forged iron, a long shank, two broad hooked crescent arms, a faceted diamond at the bottom, a ring on top, a dark iron chain and a short leather-wrapped grip with a red tassel. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects, no water, no foam, no motion lines, no speed lines, no enemies, no logos, no UI.
```

## B2. Позы форм 7–10 (ChatGPT)
Вложения: 1, 2, 3, 4 и пятым свой лист B.

```
Using Image 1 (our game character Pelag in four views), Image 2 (the same character in a real gameplay frame, for his in-game colours), Image 3 and Image 4 (his dark iron anchor, its chain and the leather-wrapped chain grip with a red tassel) and Image 5 (his Abordage key-pose sheet, poses 1 to 6), create a second key-pose sheet for the special forms of Abordage in exactly the same style, render, size and layout as Image 5: a 16:9 landscape image on a plain light grey background with a thin ground line under each row, two rows of four full-body figures of the same size with clear empty space between them, the top row in a strict side view with Pelag moving toward the right and the bottom row showing exactly the same poses from a three-quarter front view, small numbers 7 to 10 above the columns and no other text. As in Image 5, the curved sabre stays tucked in his red sash at his left hip and he never holds it, the chain grip is in his left fist and the right hand is free. Pose 7, Geyser uppercut: on arrival he explodes upward out of a low crouch, legs extending, rising onto the balls of both feet, the right fist driving straight up past his face to above his head in a rising uppercut, the left fist with the chain grip pulled down to the hip, the chest lifting and the head following the fist upward. Pose 8, Breach straight punch: a deep long lunge forward, the left foot planted flat far in front, the right leg extended behind, the right arm fully extended straight forward at chest height with the shoulder and the whole torso driven into the punch as if punching clean through the target, the left fist with the chain grip pulled back to the ribs, the head level and looking along the punch. Pose 9, Quake hop: at the end of the pull he pops into a short low hop, both knees tucked, the torso upright, the right fist raised high above his head, the left fist with the chain grip down at his side, the eyes on the ground in front of him. Pose 10, Quake slam: a hard landing in a deep wide squat, the right knee close to the ground, the right fist driven straight down into the ground in front of his feet, the left arm out to the side for balance, the back straight and the head up looking forward. The body must stay solid and athletic in every pose: the spine long and straight, chest and hips turned together, the head following the chest, knees and elbows bending only the natural way, the weight clearly on the feet. Pelag must match Image 1 exactly in every figure: tanned young islander, dark messy hair, red headband, white short-sleeve wrap shirt with small turquoise trim, red sash, wide cream knee-length trousers with red and turquoise cuffs, brown wrist wraps, brown sandals; the anchor and chain match Images 3 and 4 exactly. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects, no water, no foam, no shockwave, no cracks, no motion lines, no enemies, no logos, no UI.
```

## S. Стартовый кадр броска, игровая камера (ChatGPT)
Вложения: 1, 2, 3, 4, 5S, 6S и седьмым свой лист B.

```
Using Image 1 (our game character Pelag in four views), Image 2 (the same character in a real gameplay frame, for his in-game colours), Image 3 and Image 4 (his dark iron anchor, its chain and the leather-wrapped chain grip with a red tassel), Image 5 (a real screenshot of our game), Image 6 (our approved target frame of his skill Abordage) and Image 7 (his Abordage key-pose sheet), create one 16:9 landscape game screenshot that will be the first frame of an animation reference: the instant just before Pelag hurls his anchor. Use exactly the camera angle, lighting, ground and bright painted 3D game look of Image 5 (high top-down three-quarter view of our forest arena), framed a little closer, as if the game camera zoomed in about 1.5 times, so his pose reads clearly. Show the same arena spot and the same forest guardian enemy as in Image 6: the enemy stands on open bare ground up and to the right of Pelag, facing him, about six metres away (about three and a half of Pelag's body heights), with clear open ground between them; Pelag stands at the lower left, at the spot where his run begins in Image 6, and both of them are fully in frame with margin around them. Pelag is in pose 1 of Image 7: weight on the back right foot, the left foot forward pointing at the enemy, knees bent, the torso coiled to the right, the right hand gripping the anchor shank with the anchor cocked back just behind his right shoulder like a harpoon thrower, the left fist forward at chest height holding the chain grip, a short loop of chain hanging between his hands, his eyes on the enemy; the curved sabre stays tucked in his red sash at his left hip exactly as in Image 1. Pelag must match Image 1 exactly: tanned young islander, dark messy hair, red headband, white short-sleeve wrap shirt with small turquoise trim, red sash, wide cream knee-length trousers with red and turquoise cuffs, brown wrist wraps, brown sandals; the anchor and chain match Images 3 and 4 exactly: dark blue-grey forged iron, a long shank, two broad hooked crescent arms, a faceted diamond at the bottom, a ring on top, a dark iron chain and a short leather-wrapped grip with a red tassel. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects at all: no water, no foam, no splashes, no wake, no trails, no motion lines, no glow, no text, no UI.
```

## S2. Запасной стартовый кадр, студия (ChatGPT, по желанию)
Вложения: 1, 2, 3, 4 и пятым свой лист B.

```
Using Image 1 (our game character Pelag in four views), Image 2 (the same character in a real gameplay frame, for his in-game colours), Image 3 and Image 4 (his dark iron anchor, its chain and the leather-wrapped chain grip with a red tassel) and Image 5 (his Abordage key-pose sheet), create one 16:9 landscape still that will be the first frame of an animation reference video, rendered in the same stylized 3D game look as Image 1 as a clean model render, not an illustration. The set is a plain light grey studio: a seamless light grey floor curving into a light grey back wall, soft even light from the front and above, soft contact shadows. The camera is static, at chest height and slightly above, giving a three-quarter side view wide enough to show everything with margin. Pelag stands full body on the left quarter of the frame facing right, in pose 1 of Image 5: weight on the back right foot, the left foot forward pointing at the target, knees bent, the torso coiled to the right, the right hand gripping the anchor shank with the anchor cocked back just behind his right shoulder like a harpoon thrower, the left fist forward at chest height holding the chain grip, a short loop of chain hanging between his hands, his eyes on the target; the curved sabre stays tucked in his red sash at his left hip exactly as in Image 1. On the right, about three and a half of his body heights away, stands one plain wooden training post: a thick round wooden pole a little taller than him on a flat round base, with a short crossbar at chest height. Pelag must match Image 1 exactly: tanned young islander, dark messy hair, red headband, white short-sleeve wrap shirt with small turquoise trim, red sash, wide cream knee-length trousers with red and turquoise cuffs, brown wrist wraps, brown sandals; the anchor and chain match Images 3 and 4 exactly. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects, no water, no foam, no motion lines, no text, no logos, no UI.
```

---

## C. Если вышло не так (строку отправить в тот же чат)

**Листы поз (B, B2)**
- Персонаж уплыл: `Every figure must be the same character as Image 1: same face, red headband, white wrap shirt, red sash, cream trousers, sandals, and the curved sabre tucked in his sash at his left hip.`
- Сабля в руке: `He never holds the sabre in this skill: it stays tucked in his red sash at his left hip in every figure; his left fist holds the chain grip and his right hand is free.`
- Якорь не наш: `The anchor must be exactly the one in Images 3 and 4: dark blue-grey forged iron, long shank, two broad hooked crescent arms, faceted diamond at the bottom, ring on top, dark iron chain, leather-wrapped grip with a red tassel.`
- Замах большой или крутит цепь: `Make pose 1 a short, compact harpoon-throw wind-up: the anchor only just behind his right shoulder, no big swing and no chain twirling.`
- Цепь провисла в рывке: `In poses 3 and 4 the chain is dead straight and taut, running from his left fist straight forward toward the target.`
- Тело свернулось в комок: `Keep his body long and stretched in poses 3 and 4: one straight line from the trailing heel to the left fist, the torso leaning no more than about 45 degrees, never curled up.`
- Высоко летит: `His feet stay within a hand's height of the floor during the pull; no jumps, no flips, no spins.`
- Не та рука бьёт: `The punch is thrown with the right fist; the left fist keeps holding the chain grip.`
- Фигуры слиплись или обрезаны: `Keep all figures full body, the same size, with clear empty space between them; the chain never crosses into the next figure; nothing overlaps or is cut off.`
- Ракурсы перепутаны: `Top row strictly side view, moving to the right; bottom row the same poses from a three-quarter front view.`
- Позы ломаные или деревянные: `Make the poses athletic and natural: spine straight, chest and hips turned together, weight clearly on the planted foot, no twisted or rubbery limbs.`
- Лишний текст: `Only the small pose numbers above the columns, no other text.`

**Стартовый кадр (S, S2)**
- Камера не игровая: `Use exactly the high top-down three-quarter camera angle and the lighting of Image 5, only framed a little closer.`
- Нужен точный игровой масштаб: `Zoom out to exactly the scale of Image 5: Pelag the same size on screen as in Image 5.`
- Пелаг слишком мелкий: `Frame closer so Pelag is about one fifth of the frame height, keeping the same camera angle.`
- Появились эффекты: `Remove every effect: no water, no foam, no splashes, no trails, no glow.`
- Цель не та или далеко: `One forest guardian enemy exactly like in Image 6, about six metres from Pelag up and to the right, facing him, on open ground.`
- Якорь уже летит: `The anchor is still in his right hand, cocked behind his right shoulder; it has not been thrown yet.`

## Откуда кадры
1. Копия `squall-forms-2026-10-02/motion-ref-pack/1-pelag-model-4views.jpg` (= `dash-2026-10-02/motion-ref/dash-ref-1-identity.jpg`).
2. Копия `squall-forms-2026-10-02/motion-ref-pack/2-pelag-ingame.jpg` (вырез `artifacts/capture/pelag-dash-closeup/shot_00_t3.00s.png`).
3. Склейка `artifacts/capture/pelag-dash-closeup/zoom_anchor_stance.jpg` (сверху) и `zoom_anchor_head.jpg` (снизу), ширина 1440.
4. Копия `demo-polish-2026-09-30/references/anchor_identity.jpg`: лист, с которого делали голову якоря в Tripo (она сейчас в игре).
5B. Копия `squall-forms-2026-10-02/motion-ref-pack/B-key-poses-chatgpt.png`: лист поз Шквала, по которому собраны клипы Squall2.
5S. Копия `abordage-2026-10-02/chatgpt-refs/1-scene-game-camera.jpg`.
6S. Копия `C:\Users\d.grab\Downloads\526a1c52-0e99-4d55-abef-ee6f09baa66a.png`: целевой кадр A, ChatGPT, 02.10 21:48. Та же картинка (перекодирована) лежит в `chatgpt-results/A-base.png` с 21:54.

Почему камера S игровая, а S2 запасной. Урок рывка (Kling): при камере сверху тело получилось мелким, и реф не годился. У Шквала камера сбоку на 3/4 сработала. S сделан в игровой камере, но немного крупнее. Если видео по S не читается, используем S2.
