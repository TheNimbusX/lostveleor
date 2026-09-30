import json, pathlib, subprocess
root = pathlib.Path(__file__).resolve().parent
slots = json.loads((root / 'upload-slots.tmp.json').read_text(encoding='utf-8'))
receipt = []
for i, upload in enumerate(slots['uploads'], 1):
    path = root / 'upload-references' / f'ref_0{i}.jpg'
    data = path.read_bytes()
    process = subprocess.run(['curl.exe', '-sS', '--ssl-no-revoke', '--max-time', '90', '-X', 'PUT',
        '-H', 'Content-Type: image/jpeg', '--data-binary', '@' + str(path),
        '-w', '\nHTTP:%{http_code}', upload['upload_url']], capture_output=True, text=True)
    output = process.stdout.strip()
    status = int(output.rsplit('HTTP:', 1)[-1]) if 'HTTP:' in output else 0
    if process.returncode or status != 200:
        raise RuntimeError(f'Reference {i}: HTTP {status}, ' + output[:400] + process.stderr[:200])
    receipt.append({'reference_index': i, 'media_id': upload['media_id'], 'status': status, 'url': upload.get('url'), 'bytes': len(data)})
    print(f'Reference {i}: HTTP {status}', flush=True)
(root / 'reference-upload-receipt.json').write_text(json.dumps({'results': receipt}, indent=2), encoding='utf-8')
(root / 'upload-slots.tmp.json').unlink()

