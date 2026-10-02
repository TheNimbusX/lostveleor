# Шквал: реф движения (Sora + ChatGPT)

Нужен реф только на движение тела. Пена и всплески в кадр не идут: их строим в игре по выбранным кадрам `chatgpt-results` (trail-wavy-1, trail-zigzag-2, elusive-return-3).
В рефе: три очень быстрых низких прыжка-выпада между тремя столбами, на прилёте удар саблей (справа налево, обратным справа, снова справа налево), резкий доворот к следующей цели, в конце приземление в стойку. Тело не должно ломаться.

## Как пользоваться
1. **Стартовый кадр (ChatGPT, картинка).** Открой новый чат, приложи `1-pelag-model-4views.jpg`, затем `2-pelag-ingame.jpg`, именно в этом порядке: номер Image в промпте совпадает с порядком вложений. Вставь промпт **S** и сохрани результат (16:9). Проверь: Пелаг наш, якорь на спине, сабля в правой руке, в кадре три столба.
2. **Видео (Sora, sora.com под тем же аккаунтом).** Новое видео, горизонтальное 16:9, длительность 5 с. Загрузи стартовый кадр из шага 1 и вставь промпт **A**. Сделай 2–4 варианта. Если меньше 10 с поставить нельзя, допиши в конец промпта строку **A+**.
   Если Sora не принимает картинку, запусти один промпт A: Пелаг в нём описан. Лицо и одежда могут уплыть, но нам важна пластика.
3. **Лист ключевых поз (ChatGPT, картинка).** Открой новый чат, приложи те же 2 файла в том же порядке и вставь промпт **B**. Сделай 1–2 повтора (regenerate).
4. Если вышло не так, возьми строку из раздела **C**. В Sora её вставляют через Remix или дописывают в промпт и генерируют заново. В ChatGPT её отправляют в тот же чат.
5. Пришли мне лучшее видео (mp4) и лист поз. Клипы соберу по ним на риге v6, как рывок. Тайминг возьму из видео и сожму под игру (прыжок 0,17 с).

## Как выбрать вариант
- Скорость настоящая, без замедленной съёмки и «парения». Каждый прыжок занимает примерно 1/5 секунды.
- Прыжки низкие: голова почти не поднимается, нет сальто и вращений.
- На прилёте передняя стопа встаёт и не едет. Удар идёт из инерции прыжка.
- Удары чередуются (справа налево, слева направо, справа налево). Конец каждого удара служит замахом следующего.
- Поворот к следующей цели начинает таз, за ним корпус, голова следует за грудью.
- Корпус цельный: спина не скручивается и не гнётся назад, руки и ноги не резиновые.
- Камера стоит, монтажных склеек нет. Последний прыжок заканчивается в устойчивой стойке.

## Вложения
| # | Файл | Что это |
|---|------|---------|
| 1 | `1-pelag-model-4views.jpg` | Модель Пелага в 4 ракурсах на светлом фоне (2164x1080). Под светлую студию подходит лучше тёмного листа |
| 2 | `2-pelag-ingame.jpg` | Чистый кадр игры: Пелаг в боевой стойке, сабля в правой руке, без HUD и эффектов (1440x960, 3:2) |

---

## S. Стартовый кадр (ChatGPT)

```
Using Image 1 (our game character Pelag in four views) and Image 2 (the same character in a real gameplay frame), create one 16:9 landscape still that will be the first frame of an animation reference video. Render it in the same stylized 3D game look as Image 1, with the same shading, the same proportions and the same colours, as a clean model render and not an illustration. The set is a plain light grey studio: a seamless light grey floor curving into a light grey back wall, soft even light from the front and above, soft contact shadows under the feet and the posts. The camera is static, at chest height and slightly above, giving a three-quarter side view, wide enough to show everything below with some margin. Pelag stands full body on the left quarter of the frame in a low, ready fighting stance facing right toward the posts: feet wider than his shoulders, knees bent, weight on the balls of his feet, the curved sabre held low in his right hand and angled forward, his left hand open in front of his chest, his eyes on the first post. To his right, three plain wooden training posts stand in a shallow zig-zag across the frame. Each post is a thick round wooden pole a little taller than him, set on a flat round base, with a short crossbar at chest height. The first post is about two of his body heights ahead and slightly closer to the camera. The second is about two body heights further right and slightly farther from the camera. The third is near the right edge and slightly closer to the camera again. Pelag must match Image 1 exactly: tanned young islander, dark messy hair, red headband, white short-sleeve wrap shirt with small turquoise trim, red sash, wide cream knee-length trousers with red and turquoise cuffs, brown wrist wraps, brown sandals, a dark iron ship anchor on a chain strapped to his back, and a curved dark-steel sabre with a red-and-gold hilt and a small red tassel in his right hand. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects, no water, no text, no logos, no UI.
```

## A. Видео (Sora, 5 с)

```
Animation reference for a fast action game: one continuous 5-second shot from a single static camera, with no cuts, at real-time speed. The first frame is the uploaded image. Pelag, a stylized 3D game character, stands in a low ready stance on the left of a plain light grey studio, facing three wooden training posts set in a shallow zig-zag to his right. He is a tanned young islander with dark messy hair, a red headband, a white short-sleeve wrap shirt with turquoise trim, a red sash, wide cream knee-length trousers, brown wrist wraps and brown sandals, with a dark iron ship anchor on a chain strapped to his back and a curved sabre in his right hand. The camera stays locked at chest height in a three-quarter side view and keeps his whole body and all three posts in frame the whole time.

He performs Squall: a chain of three very fast, low lunge-leaps, one to each post, each ending in a sabre slash on arrival.
0.0 to 0.4 s: a quick dip. His knees bend, his hips sink and his chest leans toward the first post.
0.4 to 1.0 s: leap one. He explodes off his back foot and flies low and straight at the first post, his feet only a hand's height above the floor, his body leaning forward in one straight line from head to back heel, the sabre trailing low behind his right hip. He lands on his right foot in a deep lunge right next to the post and in the same instant slashes horizontally from his right to his left through the post at waist height. The post jolts.
1.0 to 1.6 s: without stopping, he pivots on the planted foot. His hips whip toward the second post, his chest and head follow, and he launches again. Leap two is the same low burst. He lands on his left foot and slashes backhand, from his left to his right, through the second post.
1.6 to 2.2 s: he pivots again and makes leap three to the third post, landing on his right foot with a forehand slash from his right to his left.
2.2 to 2.8 s: he rebounds off the last post with a short, low hop backward, lands on both feet and sinks into a wide ready stance facing the camera at three-quarter, sabre low and forward, and settles.
2.8 to 5.0 s: he holds the stance and breathes, otherwise completely still.

Movement quality: sharp, explosive and clean, like a fencer or a martial-arts sword dancer, with sudden accelerations, crisp stops and no floating. Each leap covers the gap in about a fifth of a second. Every slash comes out of the leap's momentum, and its follow-through is the wind-up for the next slash: forehand, backhand, forehand. His body stays solid and athletic the whole time. His spine stays long and straight, his chest and hips turn together, and his head follows his chest and keeps looking at the next post. His knees and elbows bend only the natural way, the planted foot sticks to the floor without sliding, and the anchor stays tight on his back. The leaps stay low, with no high jumps, no spins and no flips. There is exactly one character, with two arms, two legs and one sabre. He keeps the same look, colours and stylized 3D game render as the first frame from start to end. No visual effects, no motion-blur smears, no speed lines, no slow motion, no camera movement, no cuts, no text. He does not speak.
```

**A+** (только если Sora не даёт 5 с; дописать в конец A):
```
The shot is 10 seconds long: at 5.0 s he performs the same three-leap chain again in the opposite direction, from the third post back to the first, with the same timing and the same alternating slashes, and ends in the same ready stance, holding it to the end.
```

## B. Лист ключевых поз (ChatGPT)

```
Using Image 1 (our game character Pelag in four views) and Image 2 (the same character in a real gameplay frame), create an animation key-pose sheet for his skill Squall, a chain of very fast, low lunge-leaps between enemies, each ending in a sabre slash on arrival. Make it a 16:9 landscape image on a plain light grey background, with a thin ground line under each row, rendered in the same stylized 3D game look as Image 1. Every figure is full body and the same size, with clear empty space between figures and nothing overlapping or cropped. There are two rows of six poses in order from left to right: the top row shows each pose in a strict side view with Pelag moving toward the right, and the bottom row shows exactly the same pose from a three-quarter front view. Put small numbers 1 to 6 above the columns and no other text. Pose 1, anticipation: out of a ready stance, a quick dip, with knees bent, hips sunk low, chest leaning toward the target, the sabre low at his right hip pointing back, the left hand forward and his eyes on the target. Pose 2, launch: pushing off, with the back left leg fully straight and only its toes touching the floor, the right knee driving forward, and the whole body one straight diagonal line from head to back heel at about 40 degrees, the sabre trailing low behind and the left arm reaching forward. Pose 3, mid-lunge: flying low with both feet only a hand's height above the floor, the right knee tucked forward and the left leg trailing straight behind, the torso leaning forward but straight, the hips no higher than in pose 1, the sabre held back low and ready, the chest square to the target and the head level. Pose 4, slash on arrival to the left (forehand): the right foot planted flat in a deep lunge, the rear left leg extended with the heel lifted, hips and chest turned together toward his left, the right arm extended with the sabre mid-sweep horizontally at waist height from his right to his left, the left hand pulled back to the hip for balance, and the head steady and looking forward. Pose 5, slash on arrival to the right (backhand): the mirror footwork, with the left foot planted flat in a deep lunge and the right leg extended behind, the sabre arm sweeping from across his chest out horizontally to his right at waist height, hips and chest opening together to the right, and the left arm thrown back for balance. Pose 6, recovery: landed on both feet in a wide ready stance, knees bent to absorb the landing, hips low, chest up, the sabre low in front and angled forward, the left hand open in front, settled and balanced. The body must stay solid and athletic in every pose: the spine long and straight, chest and hips turned together, the head following the chest, knees and elbows bending only the natural way, and the weight clearly on the feet. Pelag must match Image 1 exactly in every figure: tanned young islander, dark messy hair, red headband, white short-sleeve wrap shirt with small turquoise trim, red sash, wide cream knee-length trousers with red and turquoise cuffs, brown wrist wraps, brown sandals, a dark iron ship anchor on a chain strapped to his back, and a curved dark-steel sabre with a red-and-gold hilt and a small red tassel in his right hand. Keep his colours and brightness as in Image 2: do not whiten him, no rim light, no glow. No effects, no water, no motion lines, no logos, no UI.
```

---

## C. Если вышло не так

**Видео (Sora: Remix или дописать в промпт)**
- Не наш персонаж, пропал якорь, сменилась одежда: `Keep the character exactly as in the first frame for the whole shot: same face, red headband, white wrap shirt, red sash, cream trousers, sandals; the dark iron anchor stays strapped on his back and the curved sabre stays in his right hand.`
- Лишние руки или ноги, две сабли, тело «плавится»: `Exactly one character with two arms, two legs and one sabre in every frame; the sabre never duplicates, bends or melts, and the body never morphs or blends with the posts.`
- Замедленная съёмка, плавно, «парит»: `Real-time speed, not slow motion: each leap takes about a fifth of a second, with snappy accelerations and sudden stops, no floating and no hang time; the whole three-leap chain is over by 2.5 seconds.`
- Долго стоит в начале: `He starts moving within the first half second.`
- Высокие прыжки, сальто, вращения: `Low leaps only: his feet stay within a hand's height of the floor and his head stays at about the same height; no flips, no spins, no somersaults.`
- Тело ломается (скрутка, прогиб назад, резиновые руки и ноги): `His body stays solid: the spine stays straight and long from head to hips, chest and hips turn together, the head follows the chest, knees and elbows bend only the natural way, no stretched or rubbery limbs.`
- Ноги едут: `On each arrival the front foot plants flat and sticks to the floor while he slashes; the feet never slide or skate.`
- Склейки, наезд камеры, повтор в замедлении: `One continuous shot from a single static camera: no cuts, no zoom, no camera movement, no slow-motion replay.`
- Ушёл из кадра, не тот порядок или число прыжков: `Exactly three leaps, one to each post, in order from left to right; his whole body and all three posts stay inside the frame the entire time.`
- Бьёт по воздуху: `Each slash passes through the post at waist height and the post jolts on every hit.`

**Лист поз (ChatGPT, в тот же чат)**
- Персонаж уплыл: `Every figure must be the same character as Image 1: same face, clothes, anchor on his back and curved sabre in his right hand.`
- Фигуры слиплись или обрезаны: `Keep all twelve figures full body, the same size, with clear empty space between them; nothing overlaps or is cut off.`
- Ракурсы перепутаны: `Top row strictly side view, moving to the right; bottom row the same six poses from a three-quarter front view.`
- Позы ломаные или деревянные: `Make the poses athletic and natural: spine straight, chest and hips turned together, weight clearly on the planted foot, no twisted or rubbery limbs.`
- Лишний текст: `Only the small numbers 1 to 6 above the columns, no other text.`

## Откуда кадры
1. Копия `ART/characters/pelag/dash-2026-10-02/motion-ref/dash-ref-1-identity.jpg`: те же 4 ракурса, что `ref3-pelag-model-4views.jpg`, но на светлом фоне.
2. `artifacts/capture/pelag-dash-closeup/shot_00_t3.00s.png`, вырез x 640–1360 × y 240–720, увеличен до 1440x960.

Урок прошлого рефа (рывок, Kling): камера сверху и 2 секунды стояния на месте, бег вышел медленным, реф не годился. Поэтому здесь камера сбоку на 3/4, тайминг расписан по секундам, а движение начинается в первые 0,4 с.
