"""Замена концовки B на A 3 с (выбор владельца 29.09) прямо в готовых черновиках:
контент до начала старой концовки + A 3 с; звук — тот же микс, обрезан и погашен в конце."""
import subprocess, os
from reelkit import FF, REPO

OUT = REPO + "/ART/VIDEO/reels/drafts-2026-09-29"
END = REPO + "/ART/VIDEO/reels/endcard-2026-09-29/TWR-endcard-A-3s-1080x1920.mp4"
# slug: (где кончается чистый контент, наплыв в концовку)
CUT = {'before-after': (9.0, 0.0), 'kills': (7.0, 0.0), 'boss': (9.6, 0.3)}
ENC = ['-c:v', 'libx264', '-preset', 'slow', '-crf', '18', '-pix_fmt', 'yuv420p', '-r', '30',
       '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-color_range', 'tv',
       '-movflags', '+faststart']

for slug, (cut, xf) in CUT.items():
    for lang in ('ru', 'en'):
        src = OUT + '/TWR-reel-%s-%s.mp4' % (slug, lang)
        tmp = OUT + '/_old-B-%s-%s.mp4' % (slug, lang)
        os.replace(src, tmp)
        total = cut + 3.0 - xf
        fc = ('[0:v]trim=0:%.3f,setpts=PTS-STARTPTS,fps=30,format=yuv420p,settb=1/30[c];'
              '[1:v]fps=30,format=yuv420p,settb=1/30[e];' % cut)
        fc += ('[c][e]xfade=transition=fade:duration=%.3f:offset=%.3f[v];' % (xf, cut - xf) if xf > 0
               else '[c][e]concat=n=2:v=1:a=0[v];')
        fc += '[0:a]atrim=0:%.3f,asetpts=PTS-STARTPTS,afade=t=out:st=%.3f:d=0.7[a]' % (total, total - 0.7)
        subprocess.run([FF, '-y', '-loglevel', 'error', '-i', tmp, '-i', END, '-filter_complex', fc,
                        '-map', '[v]', '-map', '[a]'] + ENC + ['-c:a', 'aac', '-b:a', '192k', src], check=True)
        silent = OUT + '/TWR-reel-%s-%s-silent.mp4' % (slug, lang)
        subprocess.run([FF, '-y', '-loglevel', 'error', '-i', src, '-map', '0:v', '-c:v', 'copy', '-an',
                        '-movflags', '+faststart', silent], check=True)
        os.remove(tmp)
        print('ok', slug, lang, '%.1fs' % total)
