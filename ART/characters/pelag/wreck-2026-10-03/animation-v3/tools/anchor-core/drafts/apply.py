"""Риг якоря (anchor-core): перенос черновиков в razlom/ — новые файлы целиком + анкерные патчи.

Ничего не пишет, пока ВСЁ не проверено: любой якорь, найденный не ровно один раз (и не наложенный раньше),
любой новый файл, который в razlom/ уже есть с другим содержимым, — отказ без единой записи.
Повторный запуск ничего не меняет (наложенный патч и совпадающий файл пропускаются).

    python apply.py --check                  только проверка (по умолчанию)
    python apply.py --out <папка>            пропатченные копии в <папка>/<путь> (проверка компиляции), razlom/ не трогается
    python apply.py --apply                  запись в razlom/: сначала снимок задетых файлов (snapshot/<время>/ + manifest.json)
    python apply.py --restore <снимок>       откат по manifest.json; отказ, если файл после наложения кто-то менял (--force — всё равно)
    --with abordage-flag                     + необязательный патч Абордажа (за флагом AnchorRigSwitches.AbordageReturn, по умолчанию выкл.)
    --without wreck-snapshot                 без PelagAnchorRig.WreckSnapshot.cs (без него риг Крушение не берёт)

Пишет r+/truncate (на части .cs в razlom 'w' падает с EINVAL), BOM и переводы строк файла сохраняет.
Не трогает Unity: ни SaveAssets, ни пересборки контроллера. Класть, только когда редактор свободен
(флаг artifacts/tools/unity-free.flag): любая запись в Assets во время чужого Play перезагружает домен.
"""
import argparse
import datetime
import glob
import hashlib
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
DRAFT_ASSETS = os.path.join(HERE, 'razlom')
SNAPSHOTS = os.path.join(HERE, '..', 'snapshot')
SNAPSHOT_FILE = 'razlom/Assets/Game.View/PelagAnchorRig.WreckSnapshot.cs'
SIM_PROPERTY = 'public WreckState Wreck'
# Поля WreckState, которые читает PelagAnchorRig.WreckSnapshot.cs, и числа WreckPhase, которые знает AnchorRigWreckInput.
# Sim Крушения пишет параллельный поток: одного свойства мало — снимок кладётся, только если есть все поля и фазы те же.
SIM_FIELDS = ['Serial', 'Phase', 'Stage', 'Side', 'StageStartTick', 'ContactTick', 'OverheadTick', 'ChargeStartTick',
              'Charge', 'WindowEndTick', 'HoldEndTick', 'ExitEndTick', 'Direction', 'ImpactPoint', 'ImpactRadius']
SIM_PHASES = {'None': 0, 'Windup': 1, 'Follow': 2, 'Window': 3, 'Charge': 4, 'Hold': 5, 'Exit': 6}


def parse(path):
    blocks, cur, section = [], None, None
    with open(path, encoding='utf-8') as f:
        for raw in f.read().split('\n'):
            line = raw.rstrip('\r')
            if line.startswith('### PATCH '):
                cur = {'name': line[10:].strip(), 'file': None, 'op': None, 'anchor': [], 'text': [], 'src': path}
                section = None
            elif cur is None:
                continue
            elif line.startswith('### FILE '):
                cur['file'] = line[9:].strip()
            elif line.startswith('### OP '):
                cur['op'] = line[7:].strip()
            elif line == '### ANCHOR':
                section = 'anchor'
            elif line == '### TEXT':
                section = 'text'
            elif line == '### END':
                if not cur['file'] or cur['op'] not in ('insert-after', 'insert-before', 'replace') or not cur['anchor']:
                    raise SystemExit('плохой блок %s в %s' % (cur['name'], path))
                blocks.append(cur)
                cur, section = None, None
            elif section:
                cur[section].append(line)
    return blocks


def read(path):
    with open(path, 'rb') as f:
        data = f.read()
    bom = data.startswith(b'\xef\xbb\xbf')
    text = data[3:].decode('utf-8') if bom else data.decode('utf-8')
    crlf = text.count('\r\n')
    nl = '\r\n' if crlf > (text.count('\n') - crlf) else '\n'
    return text, nl, bom


def encode(text, bom):
    return (b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8')


def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if os.path.exists(path):
        with open(path, 'r+b') as f:
            f.seek(0)
            f.write(data)
            f.truncate()
    else:
        with open(path, 'xb') as f:
            f.write(data)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def patch_text(text, nl, block):
    """(новый текст, 'ok'|'already') или исключение с причиной."""
    anchor = nl.join(block['anchor']) + nl
    insert = nl.join(block['text']) + nl if block['text'] else ''
    op = block['op']
    applied = {'insert-after': anchor + insert, 'insert-before': insert + anchor, 'replace': insert}[op]
    n = text.count(anchor)
    if op != 'replace' and text.count(applied) == 1:
        return text, 'already'
    # Замена может содержать сам якорь (обёртка строки): наложенность проверяется по тексту замены, а не по якорю.
    if op == 'replace' and insert and text.count(insert) == 1:
        return text, 'already'
    if n != 1:
        raise ValueError('якорь найден %d раз(а)' % n)
    i = text.index(anchor)
    if op == 'insert-after':
        return text[:i + len(anchor)] + insert + text[i + len(anchor):], 'ok'
    if op == 'insert-before':
        return text[:i] + insert + text[i:], 'ok'
    return text[:i] + insert + text[i + len(anchor):], 'ok'


def plan(groups_with, groups_without):
    patch_files = sorted(glob.glob(os.path.join(HERE, 'patches', '*.anchored.txt')))
    if 'abordage-flag' in groups_with:
        patch_files += sorted(glob.glob(os.path.join(HERE, 'patches', 'optional', '*.anchored.txt')))
    news = []
    drafts = sorted(glob.glob(os.path.join(DRAFT_ASSETS, '**', '*.cs'), recursive=True))
    drafts += sorted(glob.glob(os.path.join(HERE, 'tools', '**', '*.cs'), recursive=True))
    for path in drafts:
        rel = os.path.relpath(path, HERE).replace('\\', '/')
        if rel == SNAPSHOT_FILE and 'wreck-snapshot' in groups_without:
            continue
        news.append((rel, path))
    return patch_files, news


def block(text, header):
    """Тело { … } после header (первое вхождение) или None."""
    i = text.find(header)
    if i < 0:
        return None
    j = text.find('{', i)
    depth, k = 0, j
    while k < len(text):
        depth += {'{': 1, '}': -1}.get(text[k], 0)
        if depth == 0:
            return text[j + 1:k]
        k += 1
    return None


def sim_wreck_problems():
    """[] — снимок Крушения можно класть; иначе список причин (нет свойства, поля, другие числа фаз)."""
    texts = []
    for path in glob.glob(os.path.join(REPO, 'razlom', 'Assets', 'Game.Sim', '**', '*.cs'), recursive=True):
        with open(path, encoding='utf-8', errors='replace') as f:
            texts.append(f.read())
    sim = '\n'.join(texts)
    problems = []
    if SIM_PROPERTY not in sim:
        problems.append('нет «%s»' % SIM_PROPERTY)
    state = block(sim, 'struct WreckState')
    if state is None:
        problems.append('нет struct WreckState')
    else:
        for field in SIM_FIELDS:
            if not re.search(r'public\s+[\w.<>]+\s+(?:[\w]+\s*,\s*)*%s\b' % field, state):
                problems.append('WreckState.%s нет' % field)
    phases = block(sim, 'enum WreckPhase')
    if phases is None:
        problems.append('нет enum WreckPhase')
    else:
        for name, value in SIM_PHASES.items():
            if not re.search(r'\b%s\s*=\s*%d\b' % (name, value), phases):
                problems.append('WreckPhase.%s != %d' % (name, value))
    return problems


def build(groups_with, groups_without):
    """Всё в памяти: {rel: (новые байты, старые байты или None)} + журнал. Ошибка — SystemExit до записи."""
    patch_files, news = plan(groups_with, groups_without)
    errors, log, out = [], [], {}
    if any(rel == SNAPSHOT_FILE for rel, _ in news):
        for problem in sim_wreck_problems():
            errors.append('Game.Sim: %s — снимок Крушения не сходится, запустите с --without wreck-snapshot' % problem)
    for rel, src in news:
        with open(src, 'rb') as f:
            data = f.read()
        target = os.path.join(REPO, rel)
        old = open(target, 'rb').read() if os.path.exists(target) else None
        if old is not None and old != data:
            errors.append('%s уже есть в razlom/ и отличается от черновика' % rel)
            continue
        log.append(('new ' if old is None else 'same') + '  ' + rel)
        if old is None:
            out[rel] = (data, None)
    state = {}
    for pf in patch_files:
        for b in parse(pf):
            rel = b['file']
            target = os.path.join(REPO, rel)
            if not os.path.exists(target):
                errors.append('%s [%s]: нет файла %s' % (os.path.basename(pf), b['name'], rel))
                continue
            if rel not in state:
                text, nl, bom = read(target)
                state[rel] = [text, nl, bom, open(target, 'rb').read()]
            try:
                state[rel][0], status = patch_text(state[rel][0], state[rel][1], b)
                log.append('%-7s %-24s %s' % (status, b['name'], rel))
            except ValueError as e:
                errors.append('%s [%s]: %s в %s' % (os.path.basename(pf), b['name'], e, rel))
    for rel, (text, nl, bom, old) in state.items():
        data = encode(text, bom)
        if data != old:
            out[rel] = (data, old)
    return out, log, errors


def main():
    ap = argparse.ArgumentParser()
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument('--check', action='store_true')
    mode.add_argument('--out')
    mode.add_argument('--apply', action='store_true')
    mode.add_argument('--restore')
    ap.add_argument('--with', dest='with_', action='append', default=[])
    ap.add_argument('--without', action='append', default=[])
    ap.add_argument('--force', action='store_true')
    a = ap.parse_args()
    if a.restore:
        return restore(a.restore, a.force)
    out, log, errors = build(set(a.with_), set(a.without))
    for line in log:
        print(line)
    if errors:
        for e in errors:
            print('ОТКАЗ ' + e, file=sys.stderr)
        sys.exit(1)
    if a.out:
        for rel, (data, _) in out.items():
            if rel.endswith('.cs') and os.path.exists(os.path.join(HERE, rel)):
                continue  # новые файлы проверка компиляции берёт прямо из черновиков
            write(os.path.join(os.path.abspath(a.out), rel), data)
        print('копии: %s' % os.path.abspath(a.out))
        return
    if not a.apply:
        print('проверка пройдена: %d файл(ов) к записи' % len(out))
        return
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    snap = os.path.abspath(os.path.join(SNAPSHOTS, stamp))
    manifest = []
    for rel, (data, old) in out.items():
        if old is not None:
            write(os.path.join(snap, rel), old)
        manifest.append({'file': rel, 'before': sha(old) if old is not None else None, 'after': sha(data)})
    write(os.path.join(snap, 'manifest.json'), json.dumps(manifest, ensure_ascii=False, indent=1).encode('utf-8'))
    for rel, (data, _) in out.items():
        write(os.path.join(REPO, rel), data)
    print('записано %d файл(ов); снимок %s' % (len(out), snap))


def restore(snap, force):
    with open(os.path.join(snap, 'manifest.json'), encoding='utf-8') as f:
        manifest = json.load(f)
    problems = []
    for m in manifest:
        target = os.path.join(REPO, m['file'])
        now = sha(open(target, 'rb').read()) if os.path.exists(target) else None
        if now != m['after']:
            problems.append(m['file'])
    if problems and not force:
        for p in problems:
            print('ОТКАЗ %s менялся после наложения (--force — откатить всё равно)' % p, file=sys.stderr)
        sys.exit(1)
    for m in manifest:
        target = os.path.join(REPO, m['file'])
        if m['before'] is None:
            if os.path.exists(target):
                os.remove(target)
                meta = target + '.meta'
                if os.path.exists(meta):
                    os.remove(meta)
            print('удалён ' + m['file'])
        else:
            # Прямо из снимка в память и r+/truncate в файл: никаких временных файлов в razlom/Assets (Unity их импортирует).
            with open(os.path.join(snap, m['file']), 'rb') as f:
                data = f.read()
            if sha(data) != m['before']:
                print('ОТКАЗ снимок %s испорчен (sha256 не тот)' % m['file'], file=sys.stderr)
                sys.exit(1)
            write(target, data)
            print('возвращён ' + m['file'])


if __name__ == '__main__':
    main()
