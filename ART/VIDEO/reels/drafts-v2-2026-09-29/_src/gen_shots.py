"""shots.json for reels v2: per shot — file, what happens, key times in the mp4, zoom, quality notes.

python gen_shots.py  (reads artifacts/capture/reels-v2/<shot>, notes from notes.json next to this file)
"""
import json, os, subprocess, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from events import events

REPO = r"C:/Users/d.grab/Desktop/the-game"
CAP = REPO + "/artifacts/capture/reels-v2"
OUT = REPO + "/ART/VIDEO/reels/drafts-v2-2026-09-29/shots.json"
FFPROBE_LIKE = REPO + "/artifacts/tools/python/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe"
HERE = os.path.dirname(os.path.abspath(__file__))

# name: (group, video_start, duration, zoom, what happens)
SHOTS = {
    'kill-guardian-z07': ('kills', 2.0, 4.5, 0.7, 'Guardian (antlered forest beast) winds up, hero cuts it down with one swing; bark/leaf burst, essence wisps fly to the hero.'),
    'kill-guardian-z09': ('kills', 2.0, 4.5, 0.9, 'Same Guardian kill, wider framing.'),
    'kill-bud-z07': ('kills', 2.0, 5.0, 0.7, 'Spit-fruit Bud killed by the hero.'),
    'kill-bud-z09': ('kills', 2.0, 5.0, 0.9, 'Same Bud kill, wider framing.'),
    'kill-stonehoof-z07': ('kills', 2.0, 5.0, 0.7, 'Stonehoof (boar/bull) killed in melee.'),
    'kill-stonehoof-z09': ('kills', 2.0, 5.0, 0.9, 'Same Stonehoof kill, wider framing.'),
    'kill-thorncaster-z07': ('kills', 2.0, 5.0, 0.7, 'Thorncaster (spike shooter) killed; hero walks in from ~6 m.'),
    'kill-thorncaster-z09': ('kills', 2.0, 5.0, 0.9, 'Same Thorncaster kill, wider framing.'),
    'kill-snarer-z07': ('kills', 2.0, 5.0, 0.7, 'Root Snarer killed; hero walks in from ~6 m.'),
    'kill-snarer-z09': ('kills', 2.0, 5.0, 0.9, 'Same Root Snarer kill, wider framing.'),
    'kill-splitter-z07': ('kills', 2.0, 7.5, 0.7, 'Splitter killed -> splits into two splitlings that leap out; hero finishes them after a 1.5 s pause.'),
    'kill-splitter-z09': ('kills', 2.0, 7.5, 0.9, 'Same Splitter split, wider framing.'),
    'kill-wendigo-z07': ('kills', 2.0, 4.5, 0.7, 'Wendigo killed in melee.'),
    'kill-wendigo-z09': ('kills', 2.0, 4.5, 0.9, 'Same Wendigo kill, wider framing.'),
    'kill-swarm-z07': ('kills', 0.0, 5.0, 0.7, 'Root swarm (1 HP each) around the hero, cut down swing after swing.'),
    'kill-swarm-z09': ('kills', 0.0, 5.0, 0.9, 'Same root swarm, wider framing.'),
    'kill-swarm-pack-z07': ('kills', 2.0, 9.0, 0.7, 'Root swarm kill on a clean glade: Wendigo stand with its 2-swarm pack; Wendigo dies first, then the two root crawlers.'),
    'kill-swarm-pack-z09': ('kills', 2.0, 9.0, 0.9, 'Same Wendigo + 2 root swarm kills, wider.'),
    'after-crowd-fight': ('after', 0.5, 11.5, 0.9, 'All nine forest kinds around the hero; hero kills them one by one (1.5 s pause between) — B telegraphs and body strike signs everywhere.'),
    'after-crowd-tank': ('after', 0.5, 9.0, 1.0, 'All nine forest kinds attacking a standing hero: overlapping B telegraphs, body signs, roots, spikes.'),
    'after-wendigo-howl': ('after', 0.5, 6.0, 0.8, 'Wendigo howl (crisp ring, chevrons outward).'),
    'after-wendigo-leap': ('after', 0.3, 6.0, 0.9, 'Wendigo opening leap onto a standing hero, then claws.'),
    'after-stonehoof-charge': ('after', 0.5, 7.0, 0.9, 'Stonehoof charge lane telegraph, charge hits the standing hero (1 s stun).'),
    'after-multikill': ('after', 0.0, 6.0, 0.8, 'Root swarm multi-kill (1 HP each).'),
    'after-multikill-z05': ('after', 0.0, 4.0, 0.5, 'Root swarm multi-kill retake, tight zoom.'),
    'boss-calm-afterclear': ('boss', 5.0, 8.0, 1.2, 'Calm glade after the Guardian dies: empty arena, hero idle, ambient wisps.'),
    'boss-calm-empty': ('boss', 0.5, 8.0, 1.3, 'Normal rift arena with no enemies: hero idle at the entry, calm glade.'),
    'boss-hero-howl': ('boss', 0.5, 6.0, 0.65, 'Mob hero shot: Wendigo howl, tight.'),
    'boss-hero-charge': ('boss', 0.5, 7.0, 0.7, 'Mob hero shot: Stonehoof charge, tight.'),
    'boss-hero-roots': ('boss', 0.5, 7.0, 0.7, 'Mob hero shot: Root Snarer roots erupt from the ground under the hero (hero stands, gets rooted).'),
    'boss-hero-swarm': ('boss', 0.0, 6.0, 0.9, 'Mob hero shot: root swarm pouring over the hero (16 requested).'),
    'boss-hero-swarm-z06': ('boss', 0.0, 6.0, 0.6, 'Root swarm on the hero, retake at tight zoom.'),
    'boss-hero-wendigo-pack': ('boss', 0.3, 7.0, 0.8, 'Mob hero shot: Wendigo with two root crawlers pressing a standing hero (repeated leaps + bites).'),
    'dodge-stonehoof-z08': ('dodge', 0.5, 8.0, 0.8, 'Stonehoof charge lane appears, hero steps out of the lane before the charge.'),
    'dodge-stonehoof-z10': ('dodge', 0.5, 8.0, 1.0, 'Same Stonehoof dodge, wider (whole lane in frame).'),
    'dodge-snarer-z07': ('dodge', 0.5, 8.0, 0.7, 'Root Snarer circle under the hero, hero walks out before the roots erupt.'),
    'dodge-snarer-z09': ('dodge', 0.5, 8.0, 0.9, 'Same Root Snarer dodge, wider.'),
    'dodge-thorncaster-z08': ('dodge', 0.5, 8.0, 0.8, 'Thorncaster spike line/burst, hero sidesteps out.'),
    'dodge-thorncaster-z10': ('dodge', 0.5, 8.0, 1.0, 'Same Thorncaster dodge, wider.'),
    'dodge-wendigo-turn-z08': ('dodge', 0.5, 9.0, 0.8, 'Hero circles the Wendigo -> 360 sweep with knockback HITS the hero (4th beat: the fail).'),
    'dodge-wendigo-turn-z10': ('dodge', 0.5, 9.0, 1.0, 'Same Wendigo 360 sweep hit, wider.'),
    'dodge-wendigo-dodge-z09': ('dodge', 0.3, 8.0, 0.9, 'Wendigo leap telegraph, hero steps aside before the landing.'),
}


def main():
    notes = {}
    notes_path = os.path.join(HERE, 'notes.json')
    if os.path.exists(notes_path):
        with open(notes_path, encoding='utf-8') as f:
            notes = json.load(f)
    shots = []
    for name, (group, start, dur, zoom, what) in SHOTS.items():
        d = os.path.join(CAP, name)
        mp4 = os.path.join(d, name + '_1080x1920_60fps.mp4')
        if not os.path.exists(mp4):
            continue
        n = notes.get(name, {})
        entry = {
            'shot': name,
            'group': group,
            'file': mp4.replace('/', '\\'),
            'sheet': (mp4[:-4] + '_sheet.jpg').replace('/', '\\'),
            'size': '1080x1920', 'fps': 60, 'duration_s': dur,
            'reels_zoom': zoom,
            'what': n.get('what', what),
            'key_times': n.get('key_times', {}),
            'events_from_log': events(d, start, dur),
            'quality': n.get('quality', ''),
            'use': n.get('use', ''),
        }
        shots.append(entry)
    doc = {
        'made': '2026-09-29',
        'capture': 'capture.ps1 -Reels -NoBars -Video -SilentVideo -WorkspaceName mobsv2 (no HP bars / elite bar / name plates; damage numbers kept); script _src/run-captures-v2.ps1',
        'time_base': 'all times are seconds in the mp4 (0 = first frame); events_from_log: tick/30 - VideoStart - 0.1',
        'audio': 'captures are silent; add our own SFX from razlom/Assets/Resources/Audio/Combat/** in post',
        'shots': shots,
    }
    doc.update(notes.get('_doc', {}))
    with open(OUT, 'w', encoding='utf-8') as f:
        json.dump(doc, f, indent=1, ensure_ascii=False)
    print(OUT, len(shots))


if __name__ == '__main__':
    main()
