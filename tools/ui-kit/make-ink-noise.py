"""Бесшовный шум для шейдера «Razlom/UI Ink»: 256×256, три независимых канала.

R и G сдвигают выборку дыма (течение), B — плотность и фронт проявления.
Фильтр 1/f в частотной области даёт облачный шум, который сам собой повторяется по краям.

Запуск: python tools/ui-kit/make-ink-noise.py
"""
import os

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "razlom", "Assets", "UI", "Shaders", "ink_noise.png")
SIZE = 256


def channel(rng, beta):
    white = rng.standard_normal((SIZE, SIZE))
    fy = np.fft.fftfreq(SIZE)[:, None]
    fx = np.fft.fftfreq(SIZE)[None, :]
    radius = np.sqrt(fx * fx + fy * fy)
    radius[0, 0] = 1.0
    shaped = np.fft.ifft2(np.fft.fft2(white) / radius ** beta).real
    # Растяжка по перцентилям: без редких выбросов шум заполняет весь диапазон.
    low, high = np.percentile(shaped, [1.0, 99.0])
    return np.clip((shaped - low) / (high - low), 0.0, 1.0)


def main():
    rng = np.random.default_rng(25092026)
    rgb = np.dstack([channel(rng, 1.25), channel(rng, 1.25), channel(rng, 1.05)])
    Image.fromarray((rgb * 255 + .5).astype(np.uint8), "RGB").save(OUT)
    print(OUT)


if __name__ == "__main__":
    main()
