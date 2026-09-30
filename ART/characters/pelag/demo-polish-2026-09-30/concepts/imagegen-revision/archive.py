from pathlib import Path
import hashlib, json, shutil
from PIL import Image
root = Path(__file__).resolve().parent
path = root / 'manifest.json'
data = json.loads(path.read_text(encoding='utf-8'))
for item in data['outputs']:
    output = root / item['file']
    if output.exists():
        raise FileExistsError(output)
    shutil.copy2(item['source'], output)
    item['sha256'] = hashlib.sha256(output.read_bytes()).hexdigest()
    with Image.open(output) as image:
        item['size'] = list(image.size)
    print(item['id'], item['size'])
path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')

