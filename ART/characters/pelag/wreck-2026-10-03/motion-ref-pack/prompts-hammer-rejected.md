# Крушение: позы и стартовый кадр (ChatGPT)

Пак — реф движения тела, эффектов в нём нет. Воду, вал, горб Девятого вала и панцирь строим в игре по целевым кадрам (`../prompts.txt`, результаты лягут в `../chatgpt-results/`).

Механика (владелец 03.10): Крушение с влитым Ударом якорем. Серия из трёх тяжёлых ударов якорем: справа налево, обратный слева направо, третий — удар якорем оземь по полосе перед собой с бегущей волной. Формы (ровно 3): Волнорез — вал воды катится по полосе ~8 м и несёт врагов к концу; Девятый вал — последний удар можно держать до 1 с, отпустил — урон и ширина ×2; Водяной панцирь — во время серии водяной панцирь: серию не прервать, входящий урон меньше, в финале панцирь лопается и бьёт всех вокруг. Вкус владельца: «резкое быстрое», тело живое, без медленных раскруток.

## Руки: как сейчас в игре и как в листе
**Сейчас в коде** (проверено 03.10):
- Клипы `Pelag_AN_WreckA/WreckB/WreckFinish` (`razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/`, состояния `WreckA_v5…` по `TempoPhase`, `RazlomPelagV5AnimatorBuilder`). Исходник был в `ART/PELAG/animation/anchor-slam/Pelag_Weighted_Combat_Work.blend`, этой папки больше нет.
- Голову якоря ставит код, а не клип: `PelagAnchorSlamView.Wreck.cs` (`PoseWreck`, `WreckPayout`). Якорь летает на цепи длиной 0,9–2 м, как кистень. Рукоять цепи в левой (`ChainGripPosition`), правая держится за цепь как опора (`ChainSupportPosition`), от пояса к правой идёт ещё отрезок цепи. Получается, обе руки на цепи, за веретено якорь никто не держит.
- Касание на 2,25 м перед героем. Третий удар: голову поднимает на 2,15 м и роняет перед собой на высоту 0,28 м.
- Сабля за кушаком у левого бедра (`SetSlamOwnership` → `BeginAnchorUse`), как у всех навыков якоря.
- Удар якорем: `AnchorSlam_v5`, клип `Pelag_AN_AnchorSlam`, голова на цепи с физикой (`PelagAnchorSlamView.Physics`). Удар через плечо по полосе 4,5 × 1,2 м, замах 15 тиков.
- Sim Крушения: радиус 2,8 м, дуга ±72°, замах 6 тиков (0,2 с), этап 15 тиков, окно продолжения 24 тика.

**В листе (по брифу):** якорь в обеих руках за веретено, как тяжёлый боевой молот. Левая рука вверху, сразу под кольцом, правая на ладонь ниже. Бьющий конец — тулья с двумя рогами и ромбом. Рукоять цепи зажата в левом кулаке вместе с веретеном, поэтому от кольца висит только короткая провисшая петля. Сабля за кушаком.
Для сборки это значит: Крушению нужен режим «якорь в руке», как у Абордажа v2 (`AbordageAnchorInRightHand`), а не цепь через `PoseWreck`. Конец якоря при вытянутых руках достаёт примерно на 1,6–1,8 м от центра тела, Sim бьёт на 2,8 м. Разницу закрывает водяная дуга, или радиус надо уменьшить. Это решается в SPEC, не в листе.

## Порядок
1. **B: лист ключевых поз (база, позы 1–7).** Новый чат, приложить по порядку `1`, `2`, `3`, `4`, `5B`. Вставить промпт **B**. Сделать 1–2 повтора, лучший сохранить как `B-key-poses-chatgpt.png`.
2. **B2: позы форм (8–11).** Новый чат, приложить `1`, `2`, `3`, `4` и пятым свой лист B. Вставить промпт **B2**, сохранить как `B2-form-poses-chatgpt.png`.
3. **S: стартовый кадр в игровой камере.** Новый чат, приложить `1`, `2`, `3`, `4`, `5S` и шестым свой лист B. Вставить промпт **S**, сохранить как `S-start-frame-chatgpt.png` (16:9). Он проверяет, читается ли поза 1 сверху, и станет первым кадром видео-рефа, если появятся кредиты Higgsfield. Если к этому моменту целевой кадр Крушения уже выбран, приложить его седьмым и отправить в тот же чат строку «Та же арена, что в целевом кадре» из раздела C.
4. Если вышло не так, отправить в тот же чат строку из раздела **C**.
5. Прислать мне B, B2 и S. Клипы собираю в Blender по листам, как Шквал v2 и Абордаж v2.

Номер Image в промпте совпадает с порядком вложений. Цифра в начале имени файла и есть номер.

## Позы (номера держим до клипов)
| # | Поза | Что важно |
|---|------|-----------|
| 1 | Готовность и замах | Компактно. Якорь двумя руками поднят по диагонали за правое плечо, тулья высоко и сзади. Вес на задней правой, левая стопа впереди, корпус скручен вправо |
| 2 | Удар 1, справа налево | Плоский мах на уровне пояса. В миг касания якорь смотрит прямо вперёд на всю длину рук, плечи и таз доворачиваются влево вместе. Вес на передней левой, правая пятка оторвана |
| 3 | Связка | Инерция уносит якорь за левое бедро, тулья низко сзади. Руки у левого бедра, корпус скручен влево, правая стопа делает короткий шаг вперёд. Взведён под обратный удар |
| 4 | Удар 2, обратный слева направо | Плоский мах на уровне пояса, якорь вперёд на всю длину рук, плечи и таз доворачиваются вправо. Вес на передней правой, левая пятка оторвана |
| 5 | Замах над головой | Левая стопа шагает вперёд, обе руки вверх, якорь над головой и чуть за ней, тулья свисает за плечи. Грудь открыта, вес на задней ноге, без прыжка |
| 6 | Удар оземь | Якорь вбит прямо вниз перед собой, тулья в земле примерно на 2/3 роста впереди передней стопы. Руки прямые вдоль веретена, глубокий выпад, спина прямая, наклон ~40°, голова поднята |
| 7 | Восстановление | Выдернул якорь, встал в широкую устойчивую стойку. Якорь по диагонали через тело: руки у левого бедра, тулья низко у правой стопы |
| 8 | Девятый вал: удержание | Поза 5, но держит и копит: якорь высоко за головой, локти высоко, корпус чуть прогнут назад и скручен вправо. Очень низкая широкая стойка, вес глубоко на задней ноге, «натянутый лук» |
| 9 | Девятый вал: отпуск | Поза 6, но сильнее: очень широкий выпад, заднее колено почти у земли, тулья вбита глубже, грудь над передним коленом, обе стопы на земле |
| 10 | Водяной панцирь: упор | Удар 1 в миг касания, но врос в землю: стойка шире и ниже, стопы плашмя, плечи чуть вперёд, подбородок вниз. Его не сдвинуть |
| 11 | Волнорез: толчок | Сразу после удара оземь шагает вперёд и толкает якорь по земле перед собой, будто двигает стену: руки вперёд-вниз вдоль веретена, тулья скользит по земле, наклон ~40°, взгляд вдоль полосы |

Как это ляжет в клипы (черновик): WreckA = 1→2→3, WreckB = 3→4, WreckFinish = 5→6→7. Девятый вал: 5→8 (держим до 1 с)→9→7. Панцирь: стойка 10 поверх ударов 2 и 4. Волнорез: 6→11→7.

Цвета форм в паке не нужны, эффектов в нём нет. Для справки (03.10): база — бирюза, Волнорез — морская зелень #1FB37E, Девятый вал — индиго #4B3FD0, Водяной панцирь — жемчуг #E4EEF6. Красного, оранжевого и золотого нет: это цвета телеграфов врагов.

## Как выбрать вариант
- Пелаг наш: лицо, повязка, одежда. Сабля всё время за кушаком у левого бедра, в руках её нет.
- Якорь наш (как на Image 3 и 4): длинное веретено, два загнутых рога, гранёный ромб снизу, кольцо сверху. Размер — примерно половина роста Пелага. Цепь тёмная, рукоять обмотана кожей, с красной кисточкой.
- Во всех позах якорь в обеих руках за веретено. Его не бросают и не крутят на длинной цепи.
- По позам 2 и 4 ясно, куда идёт мах: 2 справа налево, 4 слева направо. В нижнем ряду (3/4) это должно читаться.
- Тяжесть видна в ногах и тазе: колени согнуты, стопы плашмя на опорной ноге. При этом поза резкая, без долгой раскачки.
- Ступни на земле, без прыжков и сальто. Корпус наклонён не больше чем на ~45°.
- Корпус цельный: спина прямая, грудь и таз поворачиваются вместе, голова следует за грудью, локти и колени гнутся только естественно.
- В позе 6 тулья в земле впереди, не под ногами.

## Вложения
| # | Файл | Что это | Где нужен |
|---|------|---------|-----------|
| 1 | `1-pelag-model-4views.jpg` | Модель Пелага в 4 ракурсах на светлом фоне. Сабля за кушаком, якорь на спине | B, B2, S |
| 2 | `2-pelag-ingame.jpg` | Чистый кадр игры: цвет и яркость Пелага (сабля здесь в руке, кадр только про цвет) | B, B2, S |
| 3 | `3-anchor-on-back-ingame.jpg` | Наш якорь на спине в игре: кольцо, веретено, рога, цепь у плеча | B, B2, S |
| 4 | `4-anchor-chain-design.jpg` | Лист дизайна якоря: голова, цепь, рукоять с красной кисточкой. Свечение и взрыв на нём не брать | B, B2, S |
| 5B | `5B-abordage-keyposes-format.png` | Принятый лист поз Абордажа: только раскладка и рендер. Позы и бросок на цепи оттуда не брать | B |
| 5S | `5S-scene-game-camera.jpg` | Кадр игры: камера, свет, масштаб, лесная арена, хранители леса | S |
| 6 | свой `B-key-poses-chatgpt.png` | Лист поз из шага 1 | B2 (пятым), S (шестым) |

---

## B. Лист ключевых поз, база 1–7 (ChatGPT)
Вложения: 1, 2, 3, 4, 5B.

```
Using Image 1 (our game character Pelag in four views), Image 2 (the same character in a real gameplay frame, for his in-game colours), Image 3 (in-game close-ups of his dark iron anchor strapped on his back) and Image 4 (the design sheet of that anchor, its chain and the leather-wrapped chain grip with a red tassel), create an animation key-pose sheet for his skill Wreck: a fast series of three heavy two-handed anchor blows, a forehand swing, a backhand swing and an overhead slam straight down into the ground. Use Image 5 only as the layout and render reference: the same 16:9 landscape format, plain light grey background, thin ground line under each row, two rows of seven full-body figures of the same size with clear empty space between them, the top row in a strict side view with Pelag facing right and the bottom row showing exactly the same poses from a three-quarter front view, small numbers 1 to 7 above the columns and no other text; do not copy the poses of Image 5: here he never throws the anchor and never swings it on a long chain. In every pose he holds the anchor by its shank with both hands like a heavy war hammer: the left hand at the top of the shank just under the ring, the right hand a hand's width below it, and the crown with the two hooked arms and the diamond tip is the striking end; the leather chain grip is held in his left fist together with the shank, so only a short slack loop of chain hangs from the ring; the curved sabre stays tucked in his red sash at his left hip exactly as in Image 1 and he never holds it. The anchor is big and heavy, about half his height, and every pose shows its weight carried by the legs and hips, while the motion stays sharp and fast. Pose 1, ready and loaded: weight on the back right foot, the left foot forward pointing ahead, knees bent, torso coiled to the right, the anchor held up diagonally behind his right shoulder with the crown high and back, both elbows bent and close to the body, eyes ahead; compact, no big wind-up. Pose 2, first swing at contact, travelling from his right to his left: a flat horizontal swing at waist height, the anchor pointing straight forward at full arm's length in front of him at the moment of contact, both arms extended, hips and chest turned together toward the left, weight shifted onto the planted front left foot, the back right heel lifted. Pose 3, link: the momentum carries the anchor on past his left hip, the crown trailing low behind his left hip, both hands at his left hip, the torso coiled to the left, the right foot taking a short step forward, knees bent, loaded for the backhand, eyes ahead. Pose 4, second swing at contact, a backhand travelling from his left to his right: a flat horizontal swing at waist height, the anchor pointing straight forward at full arm's length in front of him, hips and chest turned together toward the right, weight on the planted front right foot, the back left heel lifted. Pose 5, overhead wind-up for the finishing blow: the left foot steps forward, both arms raised high, the anchor lifted above and slightly behind his head with the crown hanging back behind his shoulders, the chest open and upright, weight on the back foot, knees bent, eyes on the ground ahead; no jump. Pose 6, the slam: the anchor driven straight down into the ground in front of him, the crown biting into the ground about two thirds of his height in front of his front foot, both arms straight down along the shank, a deep forward lunge on the flat front left foot, the back right leg extended with the heel lifted, the back straight and leaning forward about 40 degrees, the head up looking ahead along the ground. Pose 7, recovery: he has pulled the anchor out of the ground and settles into a wide balanced ready stance, knees bent, hips low, chest up, the anchor held diagonally across his body with both hands on the shank in front of his left hip and the crown low beside his right foot. The body must stay solid and athletic in every pose: the spine long and straight, never curled into a ball and never folded more than about 45 degrees forward, chest and hips turned together, the head following the chest, knees and elbows bending only the natural way, the weight clearly on the feet, both feet on the ground in every pose. Pelag must match Image 1 exactly in every figure: tanned young islander, dark messy hair, red headband, white short-sleeve wrap shirt with small turquoise trim, red sash, wide cream knee-length trousers with red and turquoise cuffs, brown wrist wraps, brown sandals; the anchor and chain match Images 3 and 4 exactly: dark blue-grey forged iron, a long shank, two broad hooked crescent arms, a faceted diamond at the bottom, a ring on top, a dark iron chain and a short leather-wrapped grip with a red tassel. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects, no water, no foam, no splashes, no cracks, no shockwave, no motion lines, no speed lines, no enemies, no logos, no UI.
```

## B2. Позы форм 8–11 (ChatGPT)
Вложения: 1, 2, 3, 4 и пятым свой лист B.

```
Using Image 1 (our game character Pelag in four views), Image 2 (the same character in a real gameplay frame, for his in-game colours), Image 3 and Image 4 (his dark iron anchor, its chain and the leather-wrapped chain grip with a red tassel) and Image 5 (his Wreck key-pose sheet, poses 1 to 7), create a second key-pose sheet for the special forms of Wreck in exactly the same style, render, size and layout as Image 5: a 16:9 landscape image on a plain light grey background with a thin ground line under each row, two rows of four full-body figures of the same size with clear empty space between them, the top row in a strict side view with Pelag facing right and the bottom row showing exactly the same poses from a three-quarter front view, small numbers 8 to 11 above the columns and no other text. As in Image 5, he holds the anchor by its shank with both hands like a heavy war hammer, the left hand just under the ring holding the leather chain grip as well, the right hand a hand's width below, a short slack loop of chain hanging from the ring, and the curved sabre stays tucked in his red sash at his left hip; he never holds the sabre. Pose 8, Ninth Wave hold: he holds the overhead wind-up of pose 5 to charge the blow: the anchor lifted high above and behind his head, the crown hanging low behind his back, elbows high, the torso arched slightly back and twisted a little to the right, a very low wide stance with the weight sunk deep on the back right leg, the left foot forward, knees deeply bent, loaded like a drawn bow, eyes fixed on the ground ahead, both feet flat on the ground. Pose 9, Ninth Wave release: a bigger and deeper version of the slam of pose 6: a very wide lunge, the back knee almost touching the ground, both arms fully straight driving the anchor down, the crown buried in the ground in front of him, the chest low over the front knee, the head up; both feet stay on the ground. Pose 10, Water Shell braced swing: the first swing of pose 2 at contact, travelling from his right to his left, but rooted like a rock: a wider and lower stance than pose 2, both feet flat and wider than his shoulders, knees deeply bent, shoulders hunched a little forward, chin down, the anchor pointing straight forward at full arm's length at waist height, hips and chest turned together; he looks impossible to knock over. Pose 11, Breakwater push: right after the slam he steps through and shoves the anchor forward along the ground as if pushing a wall in front of him: the right foot stepping forward, the left leg driving from behind, both arms extended forward and down along the shank, the crown skidding along the ground at arm's length ahead of him, the chest leaning forward about 40 degrees, the head up looking straight ahead along the ground. The body must stay solid and athletic in every pose: the spine long and straight, chest and hips turned together, the head following the chest, knees and elbows bending only the natural way, the weight clearly on the feet. Pelag must match Image 1 exactly in every figure: tanned young islander, dark messy hair, red headband, white short-sleeve wrap shirt with small turquoise trim, red sash, wide cream knee-length trousers with red and turquoise cuffs, brown wrist wraps, brown sandals; the anchor and chain match Images 3 and 4 exactly. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects, no water, no foam, no waves, no shell, no shockwave, no cracks, no motion lines, no enemies, no logos, no UI.
```

## S. Стартовый кадр серии, игровая камера (ChatGPT)
Вложения: 1, 2, 3, 4, 5S и шестым свой лист B.

```
Using Image 1 (our game character Pelag in four views), Image 2 (the same character in a real gameplay frame, for his in-game colours), Image 3 and Image 4 (his dark iron anchor, its chain and the leather-wrapped chain grip with a red tassel), Image 5 (a real screenshot of our game) and Image 6 (his Wreck key-pose sheet), create one 16:9 landscape game screenshot that will be the first frame of an animation reference: the instant just before Pelag's first anchor swing. Use exactly the camera angle, lighting, ground and bright painted 3D game look of Image 5 (high top-down three-quarter view of our forest arena), framed a little closer, as if the game camera zoomed in about 1.5 times, so his pose reads clearly. Pelag stands on open bare ground a little left of and below the centre of the frame; a loose pack of three forest guardian enemies exactly like those in Image 5 stands up and to the right of him, facing him, the nearest about two and a half metres away (about one and a half of Pelag's body heights) and the farthest about four metres away, with open ground between them; everyone is fully in frame with margin around them. Pelag is in pose 1 of Image 6: weight on the back right foot, the left foot forward pointing at the enemies, knees bent, the torso coiled to the right, the anchor held by its shank with both hands and raised diagonally behind his right shoulder with the crown high and back, the left hand just under the ring holding the leather chain grip as well, a short slack loop of chain hanging from the ring, his eyes on the nearest enemy; the curved sabre stays tucked in his red sash at his left hip exactly as in Image 1. Pelag must match Image 1 exactly: tanned young islander, dark messy hair, red headband, white short-sleeve wrap shirt with small turquoise trim, red sash, wide cream knee-length trousers with red and turquoise cuffs, brown wrist wraps, brown sandals; the anchor and chain match Images 3 and 4 exactly: dark blue-grey forged iron about half his height, a long shank, two broad hooked crescent arms, a faceted diamond at the bottom, a ring on top, a dark iron chain and a short leather-wrapped grip with a red tassel. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects at all: no water, no foam, no splashes, no cracks, no trails, no motion lines, no glow, no text, no UI.
```

---

## C. Если вышло не так (строку отправить в тот же чат)

**Листы поз (B, B2)**
- Персонаж уплыл: `Every figure must be the same character as Image 1: same face, red headband, white wrap shirt, red sash, cream trousers, sandals, and the curved sabre tucked in his sash at his left hip.`
- Сабля в руке: `He never holds the sabre in this skill: it stays tucked in his red sash at his left hip in every figure; both his hands are on the anchor shank.`
- Якорь в одной руке или на длинной цепи: `In every pose he holds the anchor by its shank with both hands like a war hammer, left hand just under the ring, right hand a hand's width below; it is never thrown and never swung on a long chain, only a short slack loop of chain hangs from the ring.`
- Якорь не наш: `The anchor must be exactly the one in Images 3 and 4: dark blue-grey forged iron, long shank, two broad hooked crescent arms, faceted diamond at the bottom, ring on top, dark iron chain, leather-wrapped grip with a red tassel.`
- Якорь мелкий, как игрушка: `The anchor is big and heavy, about half his height; his legs and hips carry its weight.`
- Мах не туда: `Pose 2 swings from his right to his left, pose 4 is a backhand from his left to his right; at contact the anchor points straight forward at full arm's length at waist height.`
- Удар оземь под ногами: `In pose 6 the crown hits the ground in front of him, about two thirds of his height ahead of his front foot, both arms straight along the shank.`
- Прыгает: `Both feet stay on the ground in every pose; no jumps, no flips, no spins.`
- Тело свернулось в комок: `Keep his spine long and straight, the torso leaning no more than about 45 degrees, never curled up.`
- Фигуры слиплись или обрезаны: `Keep all figures full body, the same size, with clear empty space between them; the anchor never crosses into the next figure; nothing overlaps or is cut off.`
- Ракурсы перепутаны: `Top row strictly side view, facing right; bottom row the same poses from a three-quarter front view.`
- Позы ломаные или деревянные: `Make the poses athletic and natural: spine straight, chest and hips turned together, weight clearly on the planted foot, no twisted or rubbery limbs.`
- Лишний текст: `Only the small pose numbers above the columns, no other text.`

**Стартовый кадр (S)**
- Камера не игровая: `Use exactly the high top-down three-quarter camera angle and the lighting of Image 5, only framed a little closer.`
- Пелаг слишком мелкий: `Frame closer so Pelag is about one fifth of the frame height, keeping the same camera angle.`
- Появились эффекты: `Remove every effect: no water, no foam, no splashes, no cracks, no trails, no glow.`
- Якорь на спине, а не в руках: `The anchor is in both his hands, raised diagonally behind his right shoulder, not strapped on his back.`
- Враги далеко или не те: `Three forest guardians exactly like in Image 5, up and to the right of Pelag, facing him, the nearest about two and a half metres away.`
- Та же арена, что в целевом кадре (если целевой кадр приложен седьмым): `Use the same arena spot and the same forest guardians as in Image 7, our approved target frame; Pelag stands where the blow starts in Image 7.`

## Откуда кадры
1. Копия `abordage-2026-10-02/motion-ref-pack/1-pelag-model-4views.jpg` (= `squall-forms-2026-10-02/motion-ref-pack/1-pelag-model-4views.jpg`, = `wreck-2026-10-03/chatgpt-refs/2-pelag-model-4views.jpg`).
2. Копия `abordage-2026-10-02/motion-ref-pack/2-pelag-ingame.jpg` (вырез `artifacts/capture/pelag-dash-closeup/shot_00_t3.00s.png`).
3. Копия `abordage-2026-10-02/motion-ref-pack/3-anchor-on-back-ingame.jpg` (склейка `zoom_anchor_stance.jpg` и `zoom_anchor_head.jpg`).
4. Копия `abordage-2026-10-02/motion-ref-pack/4-anchor-chain-design.jpg` (= `demo-polish-2026-09-30/references/anchor_identity.jpg`, = `wreck-2026-10-03/chatgpt-refs/3-anchor-chain-design.jpg`).
5B. Копия `abordage-2026-10-02/motion-ref-pack/B-key-poses-chatgpt.png`: лист поз Абордажа (02.10 22:10), по которому собраны клипы Abordage2.
5S. Копия `wreck-2026-10-03/chatgpt-refs/1-scene-game-camera.jpg` (= `abordage-2026-10-02/motion-ref-pack/5S-scene-game-camera.jpg`).

Почему лист Абордажа, а не Шквала: он новее, в нём уже есть якорь и цепь нашего вида, а правило «сабля за кушаком» он выдержал во всех фигурах. Его изъяны (руки в нижнем ряду, высокая поза 4) касаются поз, а позы мы оттуда не берём.
