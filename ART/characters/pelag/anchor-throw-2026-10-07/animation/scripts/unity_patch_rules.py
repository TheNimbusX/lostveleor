"""Правка PelagAnchorThrowClipRules.cs под клипы AnchorThrow2 (07.10): python unity_patch_rules.py"""
p = r'C:\Users\d.grab\Desktop\the-game\razlom\Assets\Game.View\PelagAnchorThrowClipRules.cs'
s = open(p, encoding='utf-8').read()
a = s.index('    /// <summary>Клипы Броска якоря (03.10).')
b = s.index('        // ---- выход ----')
new = open(__file__.replace('unity_patch_rules.py', 'unity_rules_body.cs.txt'), encoding='utf-8').read()
s = s[:a] + new + s[b:]
f = open(p, 'r+', encoding='utf-8', newline=''); f.seek(0); f.write(s); f.truncate(); f.close()
print("ok", len(s))
