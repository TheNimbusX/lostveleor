"""Check recorded game output and event timing for the Pelag audio pass."""
import argparse
import json
import re
import wave
from pathlib import Path
import numpy as np

def inspect(folder, required):
    log=(folder/'player.log').read_text(encoding='utf-8',errors='replace')
    assert 'Exception' not in log, 'Runtime exception in '+str(folder)
    assert '[capture-audio] samples=' in log, 'No completed audio capture'
    assert 'clipped=0' in log, 'Clipped output'
    cues=re.findall(r'\[capture-cue\] (\w+) clip=(\S+).*?dsp=([\d.,]+)',log)
    names=[item[0] for item in cues]
    for name in required: assert name in names, 'Missing cue '+name+' in '+str(folder)
    with wave.open(str(folder/'game-audio.wav'),'rb') as reader:
        rate,channels=reader.getframerate(),reader.getnchannels()
        data=np.frombuffer(reader.readframes(reader.getnframes()),dtype='<i2').astype(float)/32768
    peak=float(np.max(np.abs(data)))
    assert peak>0.001, 'Silent game capture'
    assert np.count_nonzero(abs(data)>=.9999)==0, 'PCM clipping'
    if 'BlazePrepare' in required and 'BlazeFire' in required:
        times={name:float(time.replace(',','.')) for name,clip,time in cues}
        assert 1.05 <= times['BlazeFire']-times['BlazePrepare'] <= 1.35, 'Ignition is not aligned to 36 ticks'
    result=dict(folder=str(folder),duration=len(data)/rate/channels,peak_dbfs=round(20*np.log10(peak),2),
        rms_dbfs=round(20*np.log10(np.sqrt(np.mean(data**2))),2),cues=names,clipped=0)
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder',type=Path)
    parser.add_argument('--require',nargs='*',default=[])
    parser.add_argument('--absent',nargs='*',default=[])
    args=parser.parse_args()
    result=inspect(args.folder,args.require)
    for name in args.absent: assert name not in result['cues'], 'Unexpected cancelled cue '+name
    (args.folder/'audio-check.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(result,indent=2))
