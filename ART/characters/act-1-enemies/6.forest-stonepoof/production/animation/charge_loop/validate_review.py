from pathlib import Path
from html.parser import HTMLParser
from urllib.parse import unquote,urlsplit
import json,re,subprocess,imageio_ffmpeg,hashlib
A=Path(__file__).resolve().parent;R=A.parents[1]/'review'/'animation_charge_loop'
class Links(HTMLParser):
 def __init__(self):super().__init__();self.links=[]
 def handle_starttag(self,tag,attrs):self.links.extend(v for k,v in attrs if k in ('href','src','poster') and v and not v.startswith('#'))
page=(R/'index.html').read_text(encoding='utf8');parser=Links();parser.feed(page)
assert all((R/unquote(urlsplit(x).path)).exists() for x in parser.links)
subprocess.run(['node','--check'],input=re.search(r'<script>(.*?)</script>',page,re.S).group(1).encode('utf8'),capture_output=True,check=True,timeout=15)
assets=set()
for view in ('side','game'):
 for i in range(12):assets.add(R/(view+'_frames')/f'{i:04d}.png');assets.add(R/'reference_frames'/f'{i:04d}.jpg')
 for i in range(36):assets.add(R/('transition_'+view+'_frames')/f'{i:04d}.png');assets.add(R.parent/'animation_windup_start'/'reference_frames'/f'{i:04d}.jpg')
assert all(x.exists() and x.stat().st_size>1000 for x in assets)
ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
for view in ('side','game','transition_side','transition_game'):
 for ext in ('mp4','webm'):subprocess.run([ffmpeg,'-v','error','-i',str(R/(view+'.'+ext)),'-f','null','-'],capture_output=True,check=True,timeout=30)
qa=json.loads((A/'validation.json').read_text());bake=json.loads((A/'bake_validation.json').read_text());interp=json.loads((A/'interpolation_validation.json').read_text())
assert max(x[0] for x in qa['maximum_hoof_target_error_m'].values())<.001
assert max(qa['world_plant_drift_m_at_12m_s'].values())<.002
assert qa['minimum_mesh_z_m'][0]>-.001
assert qa['bone_length_error']<.0001
assert qa['loop_seam_position_m']<.00001 and qa['launch_join_position_m']<.00001
assert bake['maximum_baked_joint_position_error_m']<.001
assert interp['maximum_interpolated_joint_error_m'][0]<.003 and interp['maximum_interpolated_rotation_error_degrees'][0]<2
assert interp['source_sha256_unchanged']
for name in ('build_loop.py','bake_loop.py','render_transition.py','validate_loop.py','validate_interpolation.py','package_review.py'):compile((A/name).read_text(encoding='utf8'),name,'exec')
manifest=json.loads((R/'manifest.json').read_text(encoding='utf8'));manifest['bake_validation']=bake;manifest['interpolation_validation']=interp
(R/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
report={'page_links_checked':len(parser.links),'frame_assets_checked':len(assets),'videos_decoded':8,'javascript_syntax':'passed','rig_numeric_checks':'passed','accepted_source_unchanged':True,'browser_runtime':'not automatically inspected: file URL blocked by browser tool policy','owner_clip_approval':False}
(R/'review_validation.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(json.dumps(report))
