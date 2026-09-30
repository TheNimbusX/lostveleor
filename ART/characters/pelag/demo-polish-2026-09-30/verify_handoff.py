"""Verify the current colour/Tripo-anchor handoff; old concepts are archived."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parent
workspace = root.parents[3]
anchor = root / 'anchor-imagegen-tripo'
manifest = json.loads((anchor / 'manifest.json').read_text(encoding='utf-8'))
assert manifest['triangles'] <= 6000
assert manifest['cost']['total_credits'] == 30
assert manifest['owner_visual_approval'] is False
body = workspace / 'razlom/Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx'
body_hash = hashlib.sha256(body.read_bytes()).hexdigest()
assert body_hash == 'cffa80a0d3aca6a601047dfead1eba7b1430b53460f1b143da14423f765a51b6'
runtime = workspace / 'razlom/Assets/Resources/Weapons/Pelag/AnchorDemo'
for filename in ('Pelag_AnchorHead_Tripo.fbx', 'Anchor_BaseColor.png', 'Anchor_NormalGL.png'):
    assert (anchor / 'export' / filename).read_bytes() == (runtime / filename).read_bytes(), filename
page = (root / 'review/index.html').read_text(encoding='utf-8')
links = re.findall(r'(?:src|href)="([^"]+)"', page)
missing = [url for url in links if not url.startswith(('http', '#')) and not (root / 'review' / url).is_file()]

captures = {}
for category in ('game', 'game-verified', 'fps', 'fps-verified', 'poses-r02'):
    for directory in sorted((root / 'colour' / category).iterdir()):
        log = directory / 'player.log'
        if not directory.is_dir() or not log.exists():
            continue
        if category == 'game' and directory.name in ('basic-roll-after', 'basic-attacks-r02',
            'slam-after', 'roll-after', 'death-after', 'basic-attack-handoff-r02'):
            # Failed screen-capture diagnostic reused this directory; old images are not evidence.
            # The ordinary attack diagnostic is obscured by the portal and is not visual proof.
            continue
        if category == 'fps' and directory.name.startswith('roll-'):
            continue
        text = log.read_text(encoding='utf-8', errors='replace')
        values = [float(x.replace(',', '.')) for x in re.findall(
            r'\[(?:anchor-slam|anchor-leap-chain)\].*? strain=([\d.,-]+)', text)]
        attachments = [float(x.replace(',', '.')) for x in re.findall(
            r'\[(?:anchor-slam|anchor-leap-chain)\].*? grip=([\d.,-]+)', text)]
        handoffs = [float(x.replace(',', '.')) for x in re.findall(
            r'\[anchor-release\].*? handoff=([\d.,-]+)', text)]
        errors = re.findall(r'^.*(?:NullReferenceException|IndexOutOfRangeException|MissingReferenceException|Shader error).*$'
            , text, flags=re.M)
        captures[category + '/' + directory.name] = {
            'chain_samples': len(values), 'max_segment_strain': max(values, default=None),
            'max_attachment_gap_m': max(attachments, default=None),
            'final_handoff_distance_m': handoffs, 'runtime_errors': errors,
            'videos': [p.name for p in directory.glob('*.mp4')],
        }
        assert not errors, (directory.name, errors)
        if values:
            assert max(values) <= .02, (directory.name, max(values))
        if attachments:
            assert max(attachments) <= .001, (directory.name, max(attachments))
        if category in ('game-verified', 'fps-verified'):
            assert handoffs, ('incomplete handoff', directory.name)
            assert len(list(directory.glob('shot_*.png'))) == 6, ('incomplete shots', directory.name)

trx = ET.parse(workspace / 'artifacts/pelag-colour-checks/presentation-final.trx')
counts = trx.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert counts['failed'] == '0'
build = (workspace / 'artifacts/pelag-colour-final-build.log').read_text(errors='replace')
assert 'Build Finished, Result: Success.' in build
result = {
    'status': 'technical-verification-passed-owner-visual-review-pending',
    'body_sha256_unchanged': body_hash, 'anchor_triangles': manifest['triangles'],
    'build': 'artifacts/pelag-colour-final-build.log', 'presentation_tests': counts,
    'captures': captures,
    'limits': ['These are fixed-frame capture runs, not a low-end PC performance measurement.',
        'Current animation/VFX quality is unchanged; owner visual approval is pending.',
        'Death still releases weapon ownership immediately: recorded root handoff distance 0.83986 m. Chain endpoint gap 0 does not imply a smooth death handoff; that is a future animation task.',
        'Original body and high-poly saber are preserved; whole-hero 40k triangle target is not achieved.'],
    'capture_clock': 'Video and shot times are relative to the end of capture warmup. CSV Time.time is absolute; compare with the offset, not by matching numeric timestamps.',
    'archived_diagnostics': 'colour/poses was recorded too late for several skills. CPU/screen/BakeMesh probes are excluded from review; no skinning or animation changes were made to the owner project.',
    'superseded_failure': 'The first ordinary-attack handoff exposed 14.327% strain. Recorded coordinates showed the upper eye inside the torso capsule. Eye/body contact alone cleared that but exposed an insufficient curved route around the body (29.891%). Final contact plus fixed stock solved both; the chain solver is unchanged. Some game-final/fps-final captures then hit the 94 s harness timeout. game-verified/fps-verified repeat the full cases with a local 480 s budget; old diagnostics are excluded.',
    'browser_render_verified': False,
    'browser_limit': 'Browser Use rejected file://; no server or other surface was used to bypass it.',
}
(root / 'colour/verification.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
missing = [url for url in links if not url.startswith(('http', '#')) and not (root / 'review' / url).is_file()]
assert not missing, missing
print('PASS:', counts['passed'], 'presentation tests, build, unchanged body, exact export copies,',
      len(captures), 'captures and', len(links), 'local links')
