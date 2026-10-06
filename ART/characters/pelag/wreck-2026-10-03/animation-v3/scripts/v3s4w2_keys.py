"""Крушение v3, Wait2: петля верха тела в окне после маха 2 — как Wait1 (v3w1_keys), но стык — конец Swing2 (продолжение вращения).

Кадр 0 = кадр 12 = Swing2 кадр N2 (последний; поза тела = Swing1 кадр 12 после полного оборота: левая на месте выпада,
правая у правого бедра R_LAND) — копия ключей из рабочего .blend Swing2. Ноги и таз не двигаются (маска верха тела).
Хват — петля gripopt_s2 --wait 2 против живого маятника от состояния запечки Swing2@N2 (голова летит влево после удара 2).
N2 — V3S4W2_N2 (по умолчанию 19 = контакт 14 + 5).
"""
import os
from v3w1_keys import *          # noqa: F401,F403 — тело, полюса, ось рукояти, сглаживание — как Wait1
import v3w1_keys as _W

CLIP = "Pelag_AN_Wreck2_Wait2"
N2 = int(os.environ.get("V3S4W2_N2", "19"))
SEAM = {0: ("Pelag_AN_Wreck2_Swing2", N2), 12: ("Pelag_AN_Wreck2_Swing2", N2)}
HINT_FROM = ("Pelag_AN_Wreck2_Swing2", N2)
N = _W.N
