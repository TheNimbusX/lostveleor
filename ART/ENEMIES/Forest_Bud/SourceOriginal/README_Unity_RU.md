# THE WAR REMAINS — Forest Bud Ranged

Пакет: четвероногий моб, FBX с четырьмя клипами, редактируемый BLEND,
отдельный плод FBX, карты 2K и необязательный компонент залпа для Unity.

## Что сделано

- Референс очищен через Higgsfield; тело и исходные PBR-карты получены через Tripo v3.1.
- Слитый сгенерированный бутон не выдержал раскрытие. Шесть лепестков, чашелистики и плодовая сердцевина перестроены в Blender. Форма и рисунок бутона поэтому отличаются от исходного арта; оригинальная модель сохранена в Source/Tripo_Original_Unrigged.fbx.
- Generic quadruped: 28 костей в FBX, 36 с управляющими костями в BLEND. Humanoid не используется.
- 26 569 треугольников у моба, максимум 4 веса на вершину. Девять мешей.
- Idle, Walk, Ranged_Attack, Death; 30 fps. Walk in-place, root неподвижен. Death содержит небольшую вертикальную коррекцию root для контакта с землёй.
- Пять костей Spawn_Fruit_01…05. Эти кости служат sockets.
- Высота в спокойной позе около 1,31 м. Масштаб «по грудь Пелага» требует сверки с моделью Пелага, которая не предоставлена.

## Импорт в Unity

1. Скопируйте ForestBudRanged.fbx, ProjectileFruit.fbx и Textures в одну папку внутри Assets. BLEND используйте как исходник в Blender; не нужно импортировать его рядом с FBX.
2. У моба в Rig выберите Animation Type = Generic, Avatar Definition = Create From This Model. Если запрошен Root node, выберите root. Optimize Game Objects оставьте выключенным, чтобы sockets были доступны.
3. В Animation проверьте четыре клипа. Названия могут иметь префикс ARM_ForestBudRanged. Переименуйте отображаемые имена в Idle, Walk, Ranged_Attack, Death. Loop Time включите только у Idle и Walk. Apply Root Motion у Animator выключите; передвижение задаёт контроллер игры.
4. Длительности: Idle 3 с, Walk 1,067 с, Ranged_Attack 2 с, Death 2,5 с. Залп — 0,933333 с от начала Ranged_Attack (кадр 29 при 30 fps). Создайте Animation Event с функцией FireFiveFruits в этой точке.
5. Для тела: Base Map = ForestBud_BaseColor.png; Normal Map = ForestBud_Normal.png (Texture Type = Normal map); Metallic = ForestBud_MetallicSmoothness.png (smoothness из alpha, sRGB выключен); Occlusion = ForestBud_Occlusion.png (sRGB выключен). ORM сохранена отдельно: R=AO, G=roughness, B=metallic. Не подключайте ORM напрямую как MetallicSmoothness.
6. Для лепестков используйте ForestBud_Petals_BaseColor.png, metallic 0, smoothness около 0,28. Для плодов — ForestBud_Fruit_BaseColor.png с такими же базовыми настройками. Внутренние стороны лепестков тёмно-красные, края кремовые, чашелистики оливковые. Выберите шейдер своего render pipeline; FBX не гарантирует автоматический перенос Blender-шейдеров в URP/HDRP.
7. Создайте prefab плода из ProjectileFruit.fbx: добавьте Rigidbody и SphereCollider, проверьте размер около 12 см. Добавьте Unity/ForestBudVolley.cs на тот объект моба, где находится Animator, и назначьте prefab плода. Aim Forward должен смотреть вперёд моба по локальной +Z. Компонент найдёт sockets по именам.
8. Залп использует углы −24°, −12°, 0°, +12°, +24° и задержку 0,012 с между плодами. Общий разброс во времени — 0,048 с. Скорость по умолчанию 12 м/с; настройте под дистанцию боя. Урон, попадания, пул объектов и логика выбора цели относятся к игровому проекту и здесь не реализованы.
9. Проверьте размер рядом с Пелагом, направления движения и залпа, материалы, отсутствие скольжения лап на нужной скорости контроллера, переходы Animator и коллайдеры.

## Проверки и ограничения

FBX повторно импортирован в чистую сцену Blender. Проверены четыре клипа, пять sockets,
веса, текстуры, неподвижность root в Idle/Walk/Attack и совпадение крайних поз Idle/Walk.
Рендеры находятся в Previews. Подробные числа — validation.json.

В Unity 6000.5.10f1 выполнен пакетный импорт: Generic Avatar валиден, обнаружены 5 sockets, 4 клипа успешно сэмплированы, C# скомпилирован. Создан ForestBudRanged.unitypackage с prefab моба, плода, материалами и Animator Controller. Игровая физика залпа и визуальный рендер Unity пока не проверены. Материалы пакета используют Built-in Standard: для URP/HDRP требуется преобразование. См. Unity_validation.txt.
Анимации созданы процедурно и требуют финального просмотра в масштабе и темпе вашей игры.
Скелет редактируется в BLEND: CTRL_* управляют лапами, POLE_* — направлением сгиба,
petal_01…06 — отдельными лепестками. Исходная геометрия Tripo сохранена отдельно для сравнения.

Расход: 30 кредитов Tripo; для Higgsfield перед запуском была подтверждена стоимость 1 кредит.
Новые платные генерации после получения базы не запускались.

