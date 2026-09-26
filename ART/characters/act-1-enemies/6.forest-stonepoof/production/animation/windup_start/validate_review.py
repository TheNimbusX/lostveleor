from pathlib import Path
from html.parser import HTMLParser
from urllib.parse import unquote,urlsplit
import json,re,subprocess,imageio_ffmpeg
a=Path(__file__).resolve().parent;p=a.parents[1];r=p/'review'/'animation_windup_start'
class Links(HTMLParser):
    def __init__(self):super().__init__();self.links=[]
    def handle_starttag(self,tag,attrs):self.links += [v for k,v in attrs if k in ('href','src','poster') and v and not v.startswith('#')]
text=(r/'index.html').read_text(encoding='utf8');parser=Links();parser.feed(text)
missing=[x for x in parser.links if not (r/unquote(urlsplit(x).path)).exists()];assert not missing,missing
subprocess.run(['node','--check'],input=re.search(r'<script>(.*?)</script>',text,re.S).group(1).encode('utf8'),capture_output=True,check=True,timeout=15)
for kind,ext in [('reference','jpg'),('side','png'),('game','png')]:
    for i in range(37):assert (r/(kind+'_frames')/(f'{i:04d}.'+ext)).exists()
ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
for view in ('side','game'):
    for ext in ('mp4','webm'):subprocess.run([ffmpeg,'-v','error','-i',str(r/(view+'.'+ext)),'-f','null','-'],capture_output=True,check=True)
qa=json.loads((a/'validation.json').read_text());bake=json.loads((a/'bake_validation.json').read_text())
assert max(x[0] for x in qa['maximum_hoof_target_error_m_and_frame'].values())<.001
assert max(qa['stationary_support_drift_m'].values())<.001
assert qa['minimum_mesh_z_m'][0]>-.001
assert qa['maximum_bone_length_relative_error']<.0001
assert bake['maximum_baked_joint_position_error_m']<.001
assert bake['maximum_baked_rotation_error_degrees']<.1
for name in ('build_clip.py','bake_clip.py','validate_clip.py','package_review.py'):compile((a/name).read_text(encoding='utf8'),name,'exec')
report={'page_links_checked':len(parser.links),'frame_assets_checked':111,'videos_decoded':4,'javascript_syntax':'passed','rig_numeric_checks':'passed','browser_runtime':'not automatically inspected: file URL blocked by browser tool policy','owner_clip_approval':False}
(r/'review_validation.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(json.dumps(report))
