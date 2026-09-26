from pathlib import Path

source=Path(__file__).resolve().parents[1]/'stress_current_rig.py'
body=source.read_text(encoding='utf-8')
body=body.replace('OUT = Path(__file__).resolve().parent / ("stress_" + Path(bpy.data.filepath).stem)',
                  'OUT = Path(__file__).resolve().parent / ("stress_" + Path(bpy.data.filepath).stem)')
exec(compile(body,str(Path(__file__).resolve()),'exec'))
