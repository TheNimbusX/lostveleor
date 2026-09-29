"""Корнеполз v2: все боевые клипы заново по рефам Higgsfield (выбор владельца 29.09).

Запуск: blender -b --factory-startup --python build_rootswarm_v2.py

Рефы: ART/characters/act-1-enemies/review/mobs-v2-concepts-2026-09-29/refs/swarm-*.mp4
(покой, суетливый бег, оттяг и укус, попадание, смерть; стартовый кадр start_swarm.png).
Модель остаётся прежней: меш, веса и скелет берутся из source/Forest_RootSwarm_v1_Idle.fbx —
копии прежнего Forest_RootSwarm@Idle.fbx, из которого игра грузит тело. Поэтому скрипт
повторяем: он не читает собственную выгрузку.

Переменные окружения:
  ROOTSWARM_V2_OUT    — куда писать FBX (по умолчанию Resources/Characters/Forest_RootSwarm;
                        черновики — мимо Assets, чтобы открытый редактор их не импортировал);
  ROOTSWARM_V2_ONLY   — подмножество клипов через запятую: Idle,Run,AttackA,AttackB,Hit,Death;
  ROOTSWARM_V2_BLEND  — куда сохранить сцену сборки (пусто — не сохранять);
  ROOTSWARM_V2_REPORT — куда писать отчёт сборки.

Контракт с игрой (не менять без правки кода):
  * 30 кадров/с, кадр Blender N = кадр Unity N (такт выгружается с 0).
  * Idle 0…120 и Run 0…12 — петли: последний кадр совпадает с первым.
  * AttackA/B 0…24: импортёр (RazlomCharacterImport.ConfigureMobClip) берёт окно 8…24.
    Кадры 0…8 — стойка. Контакт укуса — кадр 18; таблица RootSwarmSwing в
    CharacterAnimatorView кладёт на него 12-й тик замаха (Simulation.RootSwarmAttackWindupTicks).
    Выпад на 0,5 м делает Sim (RootSwarmLungeDistance) — клип остаётся на месте.
  * Hit 0…10 (0,33 с) родным темпом — RootSwarmHitPresentationDuration и RootSwarmHitSpeed.
  * Death 0…66, покой с кадра 50, играется ×2 — EnemyPresentation.asset (RootSwarm:
    ClipSeconds 66/30, RestNormalized 50/66, StateSpeed 2) и RazlomMobAnimatorBuilder.
  * Бег «едет» по земле со скоростью RUN_STRIDE за цикл; CharacterAnimatorView делит на неё
    скорость Sim (RootSwarmRunClipGroundSpeed) — ступни не скользят.
  * Проезд корня: у Run и Death XZ снимает импортёр (центр масс стоит на месте), у остальных
    поза запекается как есть, поэтому таз в них к концу клипа возвращается в стойку.

Скелет — Humanoid (аватар из каждого файла). Узлы выгружаются в позе покоя (bind pose),
как у исходного FBX: иначе Unity построил бы аватар тела со сдвинутым тазом.
"""
import bpy, math, json, os
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(r'C:/Users/d.grab/Desktop/the-game')
LIVE = ROOT / 'razlom/Assets/Resources/Characters/Forest_RootSwarm'
PROD = ROOT / 'ART/characters/act-1-enemies/1.forest-rootswarm/production'
SOURCE = PROD / 'source/Forest_RootSwarm_v1_Idle.fbx'
OUT = Path(os.environ.get('ROOTSWARM_V2_OUT', str(LIVE)))
ONLY = [s for s in os.environ.get('ROOTSWARM_V2_ONLY', '').split(',') if s]
BLEND = os.environ.get('ROOTSWARM_V2_BLEND', str(PROD / 'Forest_RootSwarm_v2.blend'))
REPORT = Path(os.environ.get('ROOTSWARM_V2_REPORT', str(PROD / 'rootswarm_v2_build.json')))
OUT.mkdir(parents=True, exist_ok=True)

FPS = 30
# ArenaView.RootSwarmScale: единица исходника в метрах игры.
GAME_SCALE = 1.05 / 0.661713

# Бег: земля уезжает назад на RUN_STRIDE единиц за цикл из RUN_FRAMES кадров.
# 0,60 / 12 × 30 × 1,5868 = 2,38 м/с — естественная скорость клипа. Sim ведёт
# моба 3,4 м/с, клип играется ×1,43: цикл 0,28 с, «суетливый» галоп по рефу.
RUN_FRAMES = 12
RUN_STRIDE = 0.60
RUN_GROUND_SPEED = RUN_STRIDE / RUN_FRAMES * FPS * GAME_SCALE

# Укус: окно импорта и ключи тиков → кадров (дублируются в CharacterAnimatorView.RootSwarmSwing).
# Замах 12 тиков: оттяг 8→14 за 6, дрожь сжатой пружины 14→16 за 4, бросок 16→18 за 2
# (родной темп), контакт 18. Восстановление 8: полёт выпада 18→22 за 4 (Sim в эти же 4 тика
# везёт тело на 0,5 м), оседание 22→24 за 4.
BITE_FIRST, BITE_LAST, BITE_CONTACT = 8, 24, 18
BITE_WINDUP = {'ticks': [0, 6, 10, 12], 'frames': [8, 14, 16, 18]}
BITE_RECOVERY = {'ticks': [0, 4, 8], 'frames': [18, 22, 24]}

M = 'mixamorig:'
# Пространство арматуры Mixamo после импорта: +Y вверх, +Z вперёд, +X влево моба.
UP, FWD, LEFT = Vector((0, 1, 0)), Vector((0, 0, 1)), Vector((1, 0, 0))
SIDES = ('Left', 'Right')

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS
bpy.ops.import_scene.fbx(filepath=str(SOURCE), use_anim=True)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh_obj = next(o for o in bpy.data.objects if o.type == 'MESH')
source_action = arm.animation_data.action
# Импорт сдвигает такт на кадр: кадр 1 Blender = кадр 0 Unity, с него рисовались рефы.
scene.frame_set(1)
bpy.context.view_layer.update()
base = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}

bones = sorted(arm.data.bones, key=lambda b: len(b.parent_recursive))
rest_rel = {b.name: (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local.copy()
            for b in bones}


def pose(basis):
    """Позы костей в пространстве арматуры — как pb.matrix, но без depsgraph."""
    result = {}
    for b in bones:
        parent = result[b.parent.name] if b.parent else Matrix.Identity(4)
        result[b.name] = parent @ rest_rel[b.name] @ basis[b.name]
    return result


def rotate_matrix(basis, name, rotation):
    """Поворот кости вокруг её головы; поворот задан в пространстве арматуры."""
    p = pose(basis)[name]
    q = p @ basis[name].inverted()
    head = p.translation
    around = Matrix.Translation(head) @ rotation.to_4x4() @ Matrix.Translation(-head)
    basis[name] = q.inverted() @ around @ q @ basis[name]


def rotate(basis, name, axis, degrees):
    if abs(degrees) > 1e-6:
        rotate_matrix(basis, name, Matrix.Rotation(math.radians(degrees), 3, axis))


def rot3(basis, name, angles):
    """(наклон вперёд, поворот влево, крен вправо) в градусах, оси арматуры."""
    pitch, yaw, roll = angles
    rotate(basis, name, LEFT, pitch)
    rotate(basis, name, UP, yaw)
    rotate(basis, name, FWD, roll)


def translate(basis, name, offset):
    p = pose(basis)[name]
    q = p @ basis[name].inverted()
    basis[name] = q.inverted() @ Matrix.Translation(offset) @ q @ basis[name]


def rot_of(m):
    return m.to_3x3().normalized()


STANCE = pose(base)
for pb in arm.pose.bones:
    error = (pb.matrix.translation - STANCE[pb.name].translation).length
    if error > 1e-5:
        raise RuntimeError(f'Своя сборка поз разошлась с Blender: {pb.name} {error}')

LEG = {s: (M + s + 'UpLeg', M + s + 'Leg', M + s + 'Foot') for s in SIDES}
ARM = {s: (M + s + 'Arm', M + s + 'ForeArm', M + s + 'Hand') for s in SIDES}
FINGERS = ('Index', 'Middle', 'Ring')


def contact_point(kind, side):
    """Точка опоры в стойке: носок у ноги, кончики когтей у лапы."""
    if kind == 'leg':
        return STANCE[M + side + 'ToeBase'].translation.copy()
    tips = [STANCE[f'{M}{side}Hand{f}3'].translation for f in FINGERS]
    return sum(tips, Vector()) / len(tips)


CONTACT = {(k, s): contact_point(k, s) for k in ('leg', 'arm') for s in SIDES}


def stance_pole(chain):
    upper, lower, end = chain
    mid = (STANCE[upper].translation + STANCE[end].translation) * .5
    return (STANCE[lower].translation - mid).normalized()


POLE = {('leg', s): stance_pole(LEG[s]) for s in SIDES} | {('arm', s): stance_pole(ARM[s]) for s in SIDES}

# Правая лапа в стойке развёрнута наружу. В беге обе лапы ставятся когтями вперёд:
# правая получает зеркальное направление левой, иначе на бегу она гребла бы вбок.
_dir_l = (CONTACT['arm', 'Left'] - STANCE[ARM['Left'][2]].translation).normalized()
_dir_r = (CONTACT['arm', 'Right'] - STANCE[ARM['Right'][2]].translation).normalized()
_mirror_l = Vector((-_dir_l.x, _dir_l.y, _dir_l.z))
RIGHT_HAND_FORWARD = _dir_r.rotation_difference(_mirror_l).to_matrix()

misses = []


CLAVICLE = {s: M + s + 'Shoulder' for s in SIDES}
# Лапы в стойке вытянуты на 97–98 % длины: любой поворот груди от опоры рвал бы цепь.
# Недостающее добирает ключица, не больше CLAVICLE_LIMIT градусов.
ARM_REACH = .985
CLAVICLE_LIMIT = 24.0


def clavicle_assist(basis, side, target):
    upper, lower, end = ARM[side]
    p = pose(basis)
    s, a = p[CLAVICLE[side]].translation, p[upper].translation
    limit = ((p[lower].translation - a).length + (p[end].translation - p[lower].translation).length) * ARM_REACH
    if (target - a).length <= limit:
        return
    full = (a - s).rotation_difference(target - s)
    lo, hi = 0.0, min(1.0, math.radians(CLAVICLE_LIMIT) / max(full.angle, 1e-6))
    if (target - (s + Quaternion().slerp(full, hi) @ (a - s))).length > limit:
        lo = hi
    else:
        for _ in range(24):
            mid = (lo + hi) * .5
            if (target - (s + Quaternion().slerp(full, mid) @ (a - s))).length > limit:
                lo = mid
            else:
                hi = mid
    rotate_matrix(basis, CLAVICLE[side], Quaternion().slerp(full, hi).to_matrix())


def solve(basis, chain, target, rot, pole, clip, frame):
    """Двухзвенный IK: конец цепи в target, ориентация конца — rot (пространство арматуры)."""
    upper, lower, end = chain
    p = pose(basis)
    a, k, c = p[upper].translation, p[lower].translation, p[end].translation
    l1, l2 = (k - a).length, (c - k).length
    reach = target - a
    d = min(max(reach.length, abs(l1 - l2) + 1e-5), (l1 + l2) * (1 - 1e-6))
    if reach.length > (l1 + l2) + 1e-4:
        misses.append((clip, frame, end, reach.length - (l1 + l2)))
    along = reach.normalized()
    bend = pole - along * pole.dot(along)
    bend.normalize()
    u = (l1 * l1 - l2 * l2 + d * d) / (2 * d)
    knee = a + along * u + bend * math.sqrt(max(0.0, l1 * l1 - u * u))
    rotate_matrix(basis, upper, (k - a).rotation_difference(knee - a).to_matrix())
    p = pose(basis)
    k, c = p[lower].translation, p[end].translation
    rotate_matrix(basis, lower, (c - k).rotation_difference(target - k).to_matrix())
    p = pose(basis)
    rotate_matrix(basis, end, rot @ rot_of(p[end]).inverted())


def limb_rotation(pitch=0.0, yaw=0.0, roll=0.0):
    r = Matrix.Identity(3)
    if pitch: r = Matrix.Rotation(math.radians(pitch), 3, LEFT) @ r
    if roll: r = Matrix.Rotation(math.radians(roll), 3, FWD) @ r
    if yaw: r = Matrix.Rotation(math.radians(yaw), 3, UP) @ r
    return r


def place(basis, kind, side, clip, frame, move=Vector(), lift=0.0, pitch=0.0, yaw=0.0, roll=0.0,
          pivot_end=0.0, pole_extra=Vector(), forward_hand=0.0):
    """Ставит стопу или лапу.

    move  — сдвиг точки опоры от стойки; lift — подъём над землёй;
    pitch — поворот вокруг оси «влево»: при pivot_end=0 вокруг точки опоры (+ отрывает
            пятку/запястье, перекат через носок), при pivot_end=1 вокруг щиколотки/запястья
            (+ носок/когти уходят вниз-назад, − тянутся вперёд); между ними — смесь,
            чтобы смена опоры не давала скачка.
    pole_extra — добавка к направлению колена/локтя; должна обнуляться вместе с позой,
            иначе стойка не повторится.
    forward_hand — доля разворота правой лапы когтями вперёд (бег).
    """
    chain = LEG[side] if kind == 'leg' else ARM[side]
    end = chain[2]
    c0 = CONTACT[kind, side]
    e0 = STANCE[end].translation
    r0 = rot_of(STANCE[end])
    if kind == 'arm' and side == 'Right' and forward_hand:
        fix = Quaternion().slerp(RIGHT_HAND_FORWARD.to_quaternion(), forward_hand).to_matrix()
        # Разворот вокруг точки опоры: когти остаются на месте, запястье обходит их.
        e0 = c0 + fix @ (e0 - c0)
        r0 = fix @ r0
    r = limb_rotation(pitch, yaw, roll)
    target = (c0 + r @ (e0 - c0)).lerp(e0, pivot_end) + move + UP * lift
    if kind == 'arm':
        clavicle_assist(basis, side, target)
    p = pose(basis)
    # Колено и локоть поворачиваются вместе с тазом и грудью, иначе при наклоне корпуса
    # сустав выворачивается в плоскость стойки.
    carrier = M + 'Hips' if kind == 'leg' else M + 'Spine2'
    carry = rot_of(p[carrier]) @ rot_of(STANCE[carrier]).inverted()
    pole = (carry @ POLE[kind, side] + pole_extra).normalized()
    solve(basis, chain, target, r @ r0, pole, clip, frame)


def curl(basis, side, degrees, thumb=0.0, weights=(.45, 1.0, .8)):
    """Сгиб когтей: + сжимает, − растопыривает. Ось сгиба — локальная X фаланги."""
    if abs(degrees) < 1e-4 and abs(thumb) < 1e-4:
        return
    for finger in FINGERS:
        for i, w in zip((1, 2, 3), weights):
            name = f'{M}{side}Hand{finger}{i}'
            rotate(basis, name, rot_of(pose(basis)[name]).col[0], degrees * w)
    for i, w in zip((1, 2, 3), (.3, .8, .6)):
        name = f'{M}{side}HandThumb{i}'
        rotate(basis, name, rot_of(pose(basis)[name]).col[0], thumb * w)


def body(basis, hips=Vector(), hips_rot=(0, 0, 0), spine=((0, 0, 0),) * 3, neck=(0, 0, 0), head=(0, 0, 0),
         shoulders=(0.0, 0.0)):
    """Корпус. Лицо (клюв и глаза) сидит на Spine2, рога — на Neck/Head.

    Пределы мышц Humanoid: у UpperChest (Spine2) ±20°, поэтому крупные наклоны
    идут через Hips, Spine и Spine1, а Spine2 держится в ±15° от стойки.
    shoulders — подъём плеч (левое, правое), градусы.
    """
    translate(basis, M + 'Hips', hips)
    rot3(basis, M + 'Hips', hips_rot)
    for name, angles in zip(('Spine', 'Spine1', 'Spine2'), spine):
        rot3(basis, M + name, angles)
    rot3(basis, M + 'Neck', neck)
    rot3(basis, M + 'Head', head)
    rotate(basis, M + 'LeftShoulder', FWD, shoulders[0])
    rotate(basis, M + 'RightShoulder', FWD, -shoulders[1])


def fresh():
    return {name: m.copy() for name, m in base.items()}


def plant_all(basis, clip, frame, legs=True, arms=True):
    if legs:
        for s in SIDES: place(basis, 'leg', s, clip, frame)
    if arms:
        for s in SIDES: place(basis, 'arm', s, clip, frame)


# ---------------------------------------------------------------- кривые

def curve(keys, t):
    """Кубика Эрмита по ключам (кадр, значение[, наклон]).

    Наклоны по умолчанию монотонные (гармоническое среднее соседних, 0 в экстремумах и на
    краях): кривая не перелетает ключи и не уползает после последнего — поза покоя смерти
    и удержания стоят ровно. Перелёт, где он нужен, задаётся отдельным ключом.
    """
    if t <= keys[0][0]: return keys[0][1]
    if t >= keys[-1][0]: return keys[-1][1]
    k = 0
    while t > keys[k + 1][0]: k += 1

    def slope(i):
        if len(keys[i]) > 2: return keys[i][2]
        if i == 0 or i == len(keys) - 1: return 0.0
        left = (keys[i][1] - keys[i - 1][1]) / (keys[i][0] - keys[i - 1][0])
        right = (keys[i + 1][1] - keys[i][1]) / (keys[i + 1][0] - keys[i][0])
        return 0.0 if left * right <= 0 else 2 * left * right / (left + right)

    x0, y0 = keys[k][:2]
    x1, y1 = keys[k + 1][:2]
    h = x1 - x0
    s = (t - x0) / h
    s2, s3 = s * s, s * s * s
    return ((2 * s3 - 3 * s2 + 1) * y0 + (s3 - 2 * s2 + s) * h * slope(k)
            + (3 * s2 - 2 * s3) * y1 + (s3 - s2) * h * slope(k + 1))


def smooth(x):
    x = min(max(x, 0.0), 1.0)
    return x * x * (3 - 2 * x)


def pulse(t, start, attack, hold, release):
    """Рывок: резкий выход (кубика с торможением), удержание, мягкий возврат. 0 вне окна."""
    if t <= start: return 0.0
    t -= start
    if t < attack: return 1 - (1 - t / attack) ** 3
    t -= attack
    if t < hold: return 1.0
    t -= hold
    if t < release: return 1 - smooth(t / release)
    return 0.0


TAU = 2 * math.pi


# ---------------------------------------------------------------- Idle

IDLE_FRAMES = 120  # 4 с, три вдоха по 40 кадров
# Рывки головы из рефа: (кадр, выход, удержание, возврат, (наклон, поворот, крен) лица).
IDLE_TWITCHES = [
    (10, 2, 14, 9, (-3.0, 11.0, 5.0)),
    (42, 2, 10, 9, (4.0, -9.0, -6.0)),
    (70, 2, 5, 7, (-5.0, 4.0, 8.0)),
    (94, 2, 4, 11, (2.0, 7.0, -3.0)),
]
# Когти: (кадр, сторона, выход, удержание, возврат, градусы).
IDLE_CLAWS = [(24, 'Left', 4, 6, 8, 26.0), (58, 'Right', 4, 5, 8, 24.0), (100, 'Left', 3, 3, 6, 16.0),
              (103, 'Right', 3, 3, 6, 14.0)]


def idle_pose(f):
    b = fresh()
    breath = .5 - .5 * math.cos(TAU * f / 40)
    sway = math.sin(TAU * f / IDLE_FRAMES)
    sway2 = math.sin(TAU * f / IDLE_FRAMES * 2)
    face = [0.0, 0.0, 0.0]
    crown = [0.0, 0.0, 0.0]
    for start, attack, hold, release, (p, y, r) in IDLE_TWITCHES:
        w = pulse(f, start, attack, hold, release)
        face = [face[0] + p * w, face[1] + y * w, face[2] + r * w]
        crown = [crown[0] + p * .8 * w, crown[1] + y * 1.4 * w, crown[2] + r * 1.2 * w]
    # Дрожь спинных шипов: короткая серия на кадрах 80–92.
    quiver = pulse(f, 80, 2, 7, 4) * math.sin(TAU * (f - 80) / 3.0)
    body(b,
         hips=Vector((.005 * sway, .004 * breath - .002, .003 * sway2)),
         hips_rot=(.8 * sway2, 1.5 * sway, 1.0 * sway2),
         spine=((-.6 * breath, .5 * sway, 0),
                (-1.6 * breath, .8 * sway + face[1] * .3, face[2] * .3 + .8 * quiver),
                (-1.2 * breath + face[0], face[1] * .7, face[2] * .7 + 1.2 * quiver)),
         neck=(crown[0] * .4 + .8 * breath, crown[1] * .4, crown[2] * .4 + 2.0 * quiver),
         head=(crown[0] * .6, crown[1] * .6, crown[2] * .6 + 1.5 * quiver),
         shoulders=(1.4 * breath, 1.4 * breath))
    plant_all(b, 'Idle', f)
    for start, side, attack, hold, release, deg in IDLE_CLAWS:
        w = pulse(f, start, attack, hold, release)
        if w: curl(b, side, deg * w, thumb=deg * .6 * w)
    return b


# ---------------------------------------------------------------- Run

# Поперечный галоп: задние чуть раньше передних, у каждой пары небольшой разнос.
# td — касание (доля цикла), beta — доля опоры, z_td — точка опоры при касании (вперёд),
# x — ширина постановки, lift — подъём в переносе.
RUN_LIMBS = {
    ('leg', 'Left'): dict(td=0.00, beta=.30, z_td=.125, x=.118, lift=.055),
    ('leg', 'Right'): dict(td=0.09, beta=.30, z_td=.115, x=-.112, lift=.055),
    ('arm', 'Left'): dict(td=0.46, beta=.36, z_td=.47, x=.215, lift=.10),
    ('arm', 'Right'): dict(td=0.57, beta=.36, z_td=.47, x=-.235, lift=.10),
}
RUN_V = RUN_STRIDE / RUN_FRAMES  # единиц за кадр


def gait(kind, side, phi):
    """Фаза конечности → (z точки опоры, подъём, наклон, в опоре ли, доля фазы)."""
    spec = RUN_LIMBS[kind, side]
    stance_len = RUN_V * spec['beta'] * RUN_FRAMES
    z_lo = spec['z_td'] - stance_len
    t = (phi - spec['td']) % 1.0
    if t < spec['beta']:
        s = t / spec['beta']
        return spec['z_td'] - stance_len * s, 0.0, s, True
    s = (t - spec['beta']) / (1 - spec['beta'])
    # Вынос: быстрый старт, плавный подход к касанию; лёгкий перенос дальше точки касания
    # и возврат на неё — лапа «загребает».
    reach = smooth(s) + .06 * math.sin(math.pi * min(1.0, s * 1.15)) * (1 if kind == 'arm' else .5)
    z = z_lo + (spec['z_td'] - z_lo) * reach
    lift = spec['lift'] * math.sin(math.pi * s) ** .8 * (1 + .25 * (1 - s))
    return z, lift, s, False


def run_pose(f):
    b = fresh()
    phi = (f % RUN_FRAMES) / RUN_FRAMES
    bob = math.cos(2 * TAU * (phi - .43))          # две фазы полёта за цикл
    rock = -math.cos(TAU * (phi - .19))            # −1 нос вверх (толчок задних), +1 нос вниз
    flex = math.cos(TAU * (phi - .95))             # +1 собран, −1 вытянут
    wig = math.sin(TAU * phi)
    body(b,
         hips=Vector((.004 * wig, -.042 + .011 * bob, .010 * math.cos(TAU * (phi - .7)))),
         hips_rot=(7 + 5 * rock - 3 * flex, 2.5 * math.sin(TAU * (phi - .5)), 2.0 * wig),
         spine=((3 * flex, -1.0 * math.sin(TAU * (phi - .5)), -.8 * wig),
                (3 + 4 * flex, -1.5 * math.sin(TAU * (phi - .5)), -1.2 * wig),
                (-8 + 2 * flex - 2.5 * rock, -1.0 * math.sin(TAU * (phi - .5)), -.8 * wig)),
         neck=(4 - 3 * rock, 0, 1.5 * wig),
         head=(-2 - 3 * rock + 2 * bob, 0, 2.0 * math.sin(TAU * (phi - .2))),
         shoulders=(2 + 2 * flex, 2 + 2 * flex))
    for (kind, side), spec in RUN_LIMBS.items():
        z, lift, s, planted = gait(kind, side, phi)
        c0 = CONTACT[kind, side]
        move = Vector((spec['x'] - c0.x, 0, z - c0.z))
        if kind == 'leg':
            if planted:
                pitch = 32 * smooth((s - .45) / .55)       # пятка отрывается, перекат через носок
                place(b, kind, side, 'Run', f, move=move, pitch=pitch)
            else:
                # Носок в переносе висит вниз-назад и поднимается к касанию.
                pitch = curve([(0, 35), (.35, 12), (.75, -12), (1, 0)], s)
                place(b, kind, side, 'Run', f, move=move, lift=lift, pitch=pitch, pivot_end=1.0)
        else:
            if planted:
                pitch = 26 * smooth((s - .4) / .6)         # запястье перекатывается через когти
                place(b, kind, side, 'Run', f, move=move, pitch=pitch, forward_hand=1.0,
                      pole_extra=Vector((.35 if side == 'Left' else -.35, .2, -.2)))
                curl(b, side, -4 + 10 * s)
            else:
                pitch = curve([(0, 28), (.3, 22), (.65, -10), (.9, -28), (1, 0)], s)
                place(b, kind, side, 'Run', f, move=move, lift=lift, pitch=pitch, pivot_end=1.0,
                      forward_hand=1.0, pole_extra=Vector((.35 if side == 'Left' else -.35, .35, -.3)))
                curl(b, side, curve([(0, 10), (.35, 32), (.75, 18), (1, -4)], s))
    return b


# ---------------------------------------------------------------- AttackA / AttackB

def bite_pose(f, variant):
    """Укус: оттяг назад-вниз (8→14), дрожь сжатой пружины (14→16), бросок (16→18),
    контакт 18 — лицо (оно на Spine2, челюсти в скелете нет) вскидывается на 17 и
    захлопывается на 18; полёт выпада (18→22), лапы и ноги касаются земли к 22, оседание к 24.
    A тянется левой лапой и доворачивает влево, B — правой и вправо; стойка на входе и выходе общая."""
    b = fresh()
    side = 1.0 if variant == 'A' else -1.0          # A — тянется левой, B — правой
    coil = curve([(8, 0), (10, .4), (12, .85), (13.5, 1.0), (16, 1.08, .02), (17, .2), (18, 0), (24, 0)], f)
    ext = curve([(8, 0), (16, 0), (17, .6), (18, 1.0), (19, .96), (20, .84), (21, .58), (22, .3), (23, .08),
                 (24, 0)], f)
    snap = curve([(8, 0), (16, 0), (16.8, -1.0), (17.4, -.6), (18, 1.0), (19, .75), (21, .2), (23, 0)], f)
    tremble = (1 if int(round(f)) % 2 else -1) * pulse(f, 13.5, 1, 1.5, 1.5)
    body(b,
         hips=Vector((.004 * side * ext, -.045 * coil + .034 * ext, -.075 * coil + .085 * ext)),
         hips_rot=(6 * coil + 10 * ext, 3 * side * coil - 4 * side * ext, 1.5 * side * coil),
         spine=((6 * coil - 6 * ext, 2 * side * coil, .6 * tremble),
                (9 * coil - 10 * ext, -3 * side * ext, .9 * tremble - 3 * side * ext),
                (-8 * coil + 9 * snap, 4 * side * coil + 6 * side * ext, -4 * side * ext)),
         neck=(10 * coil - 4 * ext, 2 * side * ext, 1.5 * tremble),
         head=(-6 * coil + 8 * ext, 4 * side * ext, 4 * side * ext),
         shoulders=(7 * coil - 3 * ext, 7 * coil - 3 * ext))
    # Задние: упираются до броска, отталкиваются (пятка вверх), в полёте поджимаются
    # и приземляются в стойку к 22.
    push = curve([(8, 0), (16, 0), (17.5, .8), (18.5, 1.0), (19.5, .6), (21, .15), (22, 0)], f)
    air_legs = curve([(8, 0), (18.4, 0), (19.5, .9), (20.5, 1.0), (21.5, .35), (22, 0)], f)
    for s in SIDES:
        place(b, 'leg', s, f'Attack{variant}', f, move=FWD * (.04 * air_legs), lift=.045 * air_legs,
              pitch=30 * push)
    # Передние: стоят в оттяге (локти вверх), в броске уходят вперёд-вверх, когти растопырены,
    # ведущая лапа тянется дальше; касание к 21–22 в точку стойки.
    for s in SIDES:
        lead = 1.0 if (s == 'Left') == (side > 0) else .9
        reach = curve([(8, 0), (16.2, 0), (17, .55), (18, 1.0), (19, 1.0), (20, .75), (21, .25), (21.8, 0),
                       (24, 0)], f) * lead
        drop = curve([(20, 0), (21, .6), (21.8, 1.0), (22.5, .6), (24, 0)], f)
        inward = (-.015 if s == 'Left' else .14)   # правая стоит врастопырку — в броске сводится к центру
        place(b, 'arm', s, f'Attack{variant}', f,
              move=Vector((inward * reach, 0, .17 * reach - .02 * coil)),
              lift=.10 * reach - .004 * drop,
              pitch=-40 * reach + 8 * coil, pivot_end=min(1.0, reach * 2.5),
              pole_extra=Vector((.5 if s == 'Left' else -.5, .8, 0)) * max(coil, reach))
        curl(b, s, -16 * reach + 14 * coil, thumb=-10 * reach)
    return b


# ---------------------------------------------------------------- Hit

def hit_pose(f):
    """Отдача по рефу: корпус встаёт на дыбы, лицо запрокидывается, лапы вскинуты, возврат к 10."""
    b = fresh()
    r = curve([(0, 0), (1, .6), (2, .95), (3, 1.0), (4, .85), (5, .62), (6, .38), (7, .18), (8, .06), (9, 0),
               (10, 0)], f)
    h = curve([(0, 0), (1, .85), (2, 1.0), (3, .72), (4, .42), (5, .16), (6, -.05), (7, -.1), (8, -.04),
               (9, 0), (10, 0)], f)
    a = curve([(0, 0), (1, .45), (2, .85), (3, 1.0), (4, .9), (5, .68), (6, .42), (7, .18), (8, .03), (9, 0),
               (10, 0)], f)
    body(b,
         hips=Vector((0, -.026 * r, -.036 * r)),
         hips_rot=(-10 * r, 5 * r, 2 * r),
         spine=((-10 * r, 3 * r, 1 * r), (-14 * r, 4 * r, 2 * r), (-13 * h, 2 * r, 5 * h)),
         neck=(-4 * h, 3 * h, 4 * h),
         head=(-16 * h, 6 * h, 8 * h),
         shoulders=(7 * a, 7 * a))
    plant_all(b, 'Hit', f, arms=False)
    for s in SIDES:
        out = .045 if s == 'Left' else -.035
        place(b, 'arm', s, 'Hit', f, move=Vector((out * a, 0, -.04 * a)), lift=.16 * a,
              pitch=-28 * a, pivot_end=min(1.0, a * 2.5),
              pole_extra=Vector((.6 if s == 'Left' else -.6, .3, 0)) * a)
        curl(b, s, -14 * a, thumb=-8 * a)
    return b


# ---------------------------------------------------------------- Death

def death_pose(f):
    """Смерть по рефу: удар — на дыбы с раскинутыми лапами (0→8), шатается (8→24), ноги
    подламываются и тело падает ничком (24→38), отскок и оседание (38→50), дальше покой.
    Играется ×2: до покоя 0,83 с."""
    b = fresh()
    rear = curve([(0, 0), (2, .7), (4, 1.05), (7, 1.18), (12, 1.1), (18, 1.0), (24, .92), (29, .45), (33, 0),
                  (66, 0)], f)
    snap = curve([(0, 0), (1, .8), (2, 1.1), (4, 1.0), (8, .7), (14, .5), (24, .4), (30, 0), (66, 0)], f)
    fling = curve([(0, 0), (3, .7), (6, 1.0), (9, 1.05), (16, .8), (24, .6), (30, .3), (34, 0), (66, 0)], f)
    fall = curve([(0, 0), (24, 0), (27, .08), (31, .35, .1), (36, .88, .12), (38, 1.03, 0), (41, .96),
                  (44, 1.01), (47, 1.0, 0), (50, 1.0, 0), (66, 1.0, 0)], f)
    env = smooth((f - 7) / 3) * (1 - smooth((f - 24) / 6))
    wobble = math.sin(TAU * (f - 8) / 11) * env
    loll = math.sin(TAU * (f - 10) / 13) * env
    body(b,
         hips=Vector((.03 * wobble, -.026 * rear - .09 * fall, -.035 * rear - .02 * fall)),
         hips_rot=(-8 * rear + 9 * fall, 4 * rear + 3 * fall, 9 * wobble + 4 * fall),
         spine=((-10 * rear + 6 * fall, 3 * rear, 4 * wobble),
                (-13 * rear + 6 * fall, 4 * rear - 3 * fall, 5 * wobble),
                (-13 * snap - 5 * fall, 3 * loll, 6 * snap + 4 * loll + 5 * fall)),
         neck=(-4 * snap + 4 * fall, 4 * loll, 5 * loll),
         head=(-18 * snap + 2 * fall, 8 * loll + 6 * fall, 10 * loll + 14 * fall),
         shoulders=(8 * fling - 6 * fall, 8 * fling - 6 * fall))
    # Ноги: стоят, пока тело на дыбах; при падении разъезжаются назад-в-стороны лягушкой.
    for s in SIDES:
        out = 1.0 if s == 'Left' else -1.0
        splay = smooth((f - 26) / 12)
        place(b, 'leg', s, 'Death', f, move=Vector((.05 * out * splay, 0, -.10 * splay)),
              lift=.004 * splay, pitch=0, roll=-18 * out * splay,
              pole_extra=Vector((.8 * out * splay, 0, 0)))
    # Лапы: вскинуты в стороны, потом падают вперёд-в-стороны плашмя, когти врастопырку.
    for s in SIDES:
        out = 1.0 if s == 'Left' else -1.0
        splat = smooth((f - 30) / 8)
        fly = fling * (1 - splat)
        place(b, 'arm', s, 'Death', f,
              move=Vector((.07 * out * fly + .06 * out * splat, 0, -.05 * fly + .03 * splat)),
              lift=.15 * fly - .015 * splat,
              pitch=-30 * fly - 12 * splat, roll=10 * out * splat, pivot_end=min(1.0, (fly + splat) * 2.5),
              pole_extra=Vector((.7 * out * (fly + splat), .4 * fly, 0)))
        curl(b, s, -16 * fly + 20 * splat - 6 * smooth((f - 44) / 4), thumb=-10 * fly + 10 * splat)
    return b


# ---------------------------------------------------------------- сборка и выгрузка

CLIPS = {
    'Idle': (IDLE_FRAMES + 1, idle_pose),
    'Run': (RUN_FRAMES + 1, run_pose),
    'AttackA': (BITE_LAST + 1, lambda f: bite_pose(f, 'A')),
    'AttackB': (BITE_LAST + 1, lambda f: bite_pose(f, 'B')),
    'Hit': (11, hit_pose),
    'Death': (67, death_pose),
}
LOOPS = {'Idle', 'Run'}

arm.animation_data.action = None
bpy.data.actions.remove(source_action)
for pb in arm.pose.bones:
    pb.rotation_mode = 'QUATERNION'

report = {'fps': FPS, 'game_scale': GAME_SCALE, 'run_ground_speed_mps': RUN_GROUND_SPEED,
          'run_frames': RUN_FRAMES, 'run_stride_units': RUN_STRIDE,
          'bite': {'window': [BITE_FIRST, BITE_LAST], 'contact_frame': BITE_CONTACT,
                   'windup': BITE_WINDUP, 'recovery': BITE_RECOVERY}, 'clips': {}}

for name, (frames, builder) in CLIPS.items():
    if ONLY and name not in ONLY:
        continue
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    arm.animation_data.action = action
    previous = {}
    poses = []
    for frame in range(frames):
        # Петли: последний кадр — ровно первый, чтобы шов не дёргал позу.
        b = builder(0 if (name in LOOPS and frame == frames - 1) else frame)
        poses.append(pose(b))
        for pb in arm.pose.bones:
            pb.matrix_basis = b[pb.name]
            q = pb.rotation_quaternion.copy()
            # Непрерывность знака: q и −q — одна поза, но интерполяция между ними крутит кость.
            if pb.name in previous and previous[pb.name].dot(q) < 0:
                q.negate()
                pb.rotation_quaternion = q
            previous[pb.name] = q
            pb.keyframe_insert('location', frame=frame, group=pb.name)
            pb.keyframe_insert('rotation_quaternion', frame=frame, group=pb.name)
            pb.keyframe_insert('scale', frame=frame, group=pb.name)
    # Кадр вне такта с позой покоя: выгрузка пишет узлы в позе текущего кадра, а аватар
    # тела Unity строит по узлам. Так узлы совпадают с исходным FBX.
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
        pb.keyframe_insert('location', frame=-5, group=pb.name)
        pb.keyframe_insert('rotation_quaternion', frame=-5, group=pb.name)
        pb.keyframe_insert('scale', frame=-5, group=pb.name)

    first, last = poses[0], poses[-1]
    seam = max((first[n].translation - last[n].translation).length for n in first)
    stance_err, stance_worst = max(((first[n].translation - STANCE[n].translation).length, n) for n in first)
    hips = [p[M + 'Hips'].translation - STANCE[M + 'Hips'].translation for p in poses]
    report['clips'][name] = {
        'frames': [0, frames - 1], 'loop': name in LOOPS,
        'seam_error': seam if name in LOOPS else None, 'last_frame_vs_stance': None if name in LOOPS or name == 'Death'
        else max((last[n].translation - STANCE[n].translation).length for n in last),
        'first_frame_vs_stance': stance_err, 'first_frame_worst_bone': stance_worst,
        'hips_forward_range': [min(h.z for h in hips), max(h.z for h in hips)],
        'hips_up_range': [min(h.y for h in hips), max(h.y for h in hips)],
        'ik_misses': len([m for m in misses if m[0] == name]),
    }

    scene.frame_start, scene.frame_end = 0, frames - 1
    scene.frame_set(-5)
    out = OUT / f'Forest_RootSwarm@{name}.fbx'
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    mesh_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=str(out), use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        use_mesh_modifiers=False, mesh_smooth_type='FACE', add_leaf_bones=False, armature_nodetype='NULL',
        use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1, bake_anim_simplify_factor=0, path_mode='STRIP', embed_textures=False)
    print('ROOTSWARM_V2_CLIP', name, out)

report['ik_misses'] = [(c, f, e, round(d, 4)) for c, f, e, d in misses[:40]]
report['ik_miss_count'] = len(misses)
scene.frame_set(0)
if BLEND:
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
REPORT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf8')
print('ROOTSWARM_V2_READY', json.dumps({k: v for k, v in report.items() if k != 'ik_misses'}, ensure_ascii=False))
