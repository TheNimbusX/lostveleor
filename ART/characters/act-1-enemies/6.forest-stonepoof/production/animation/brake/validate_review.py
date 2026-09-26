from pathlib import Path
from html.parser import HTMLParser
from urllib.parse import unquote,urlsplit
import json,re,subprocess,imageio_ffmpeg,hashlib
A=Path(__file__).resolve().parent;R=A.parents[1]/'review'/'animation_brake';RUN=R.parent/'animation_charge_loop'
class Links(HTMLParser):
 def __init__(self):super().__init__();self.links=[]
 def handle_starttag(self,tag,attrs):self.links.extend(v for k,v in attrs if k in ('href','src','poster') and v and not v.startswith('#'))
page=(R/'index.html').read_text(encoding='utf8');parser=Links();parser.feed(page)
assert all((R/unquote(urlsplit(x).path)).exists() for x in parser.links)
subprocess.run(['node','--check'],input=re.search(r'<script>(.*?)</script>',page,re.S).group(1).encode('utf8'),capture_output=True,check=True,timeout=15)
assets=set()
for view in ('side','game'):
 for i in range(19):assets.add(R/(view+'_frames')/f'{i:04d}.png');assets.add(R/'reference_frames'/f'{i:04d}.jpg')
 for i in range(12):assets.add(RUN/(view+'_frames')/f'{i:04d}.png');assets.add(RUN/'reference_frames'/f'{i:04d}.jpg')
 for i in range(36):assets.add(RUN/('transition_'+view+'_frames')/f'{i:04d}.png');assets.add(R.parent/'animation_windup_start'/'reference_frames'/f'{i:04d}.jpg')
assert all(x.exists() and x.stat().st_size>1000 for x in assets)
ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
for view in ('side','game','transition_side','transition_game'):
 for ext in ('mp4','webm'):subprocess.run([ffmpeg,'-v','error','-i',str(R/(view+'.'+ext)),'-f','null','-'],capture_output=True,check=True,timeout=30)
qa=json.loads((A/'validation.json').read_text());bake=json.loads((A/'bake_validation.json').read_text());interp=json.loads((A/'interpolation_validation.json').read_text())
assert max(x[0] for x in qa['maximum_hoof_target_error_m'].values())<.001
assert max(qa['post_stop_support_drift_m'].values())<.001
assert qa['minimum_mesh_z_m'][0]>-.001
assert max(abs(z) for bounds in qa['skid_sole_z_ranges_m'].values() for z in bounds)<.007
assert qa['bone_length_error']<.0001
assert max(j['max_joint_position_m'] for j in qa['joins'].values())<.00001
assert bake['maximum_baked_joint_position_error_m']<.001
assert interp['maximum_interpolated_joint_error_m'][0]<.003 and interp['maximum_interpolated_rotation_error_degrees'][0]<2
assert interp['source_sha256_unchanged'] and qa['accepted_sources_unchanged']
for name in ('build_brake.py','bake_brake.py','validate_brake.py','validate_interpolation.py','package_review.py'):compile((A/name).read_text(encoding='utf8'),name,'exec')
report={'page_links_checked':len(parser.links),'frame_assets_checked':len(assets),'videos_decoded':8,'javascript_syntax':'passed','rig_numeric_checks':'passed','accepted_sources_unchanged':True,'browser_runtime':'not automatically inspected: file URL blocked by browser tool policy','owner_clip_approval':False}
(R/'review_validation.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(json.dumps(report))
