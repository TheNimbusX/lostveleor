<#
    Убирает запечённый ореол вокруг арта с прозрачным фоном и обрезает по содержимому.

    У `ART/characters/pelag/a pose.png` вокруг фигуры полупрозрачное тёплое
    свечение — на синей панели палатки оно читается как грязный ореол. Пиксели
    с альфой ниже Floor становятся прозрачными, между Floor и Ceiling альфа
    растягивается до полной — край остаётся мягким, а дымка исчезает.

    Пример:
        & ./tools/ui-kit/clean-halo.ps1 -Source 'ART/characters/pelag/a pose.png' `
            -Target 'razlom/Assets/Resources/UI/Tent/PelagArt.png'
#>
param(
    [Parameter(Mandatory)] [string] $Source,
    [Parameter(Mandatory)] [string] $Target,
    [int] $Floor = 150,
    [int] $Ceiling = 235,
    [int] $Padding = 8
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('HaloCleaner' -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class HaloCleaner
{
    public static string Clean(string input, string output, int floor, int ceiling, int padding)
    {
        using (var loaded = new Bitmap(input))
        using (var bmp = loaded.Clone(new Rectangle(0, 0, loaded.Width, loaded.Height), PixelFormat.Format32bppArgb))
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var px = new byte[w * h * 4];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    int a = px[i + 3];
                    int na = a <= floor ? 0 : a >= ceiling ? 255 : (a - floor) * 255 / Math.Max(1, ceiling - floor);
                    px[i + 3] = (byte)na;
                    if (na == 0) { px[i] = px[i + 1] = px[i + 2] = 0; continue; }
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
            if (maxX < 0) throw new InvalidOperationException("После чистки не осталось ни одного пикселя");
            minX = Math.Max(0, minX - padding); minY = Math.Max(0, minY - padding);
            maxX = Math.Min(w - 1, maxX + padding); maxY = Math.Min(h - 1, maxY + padding);
            var rect = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            using (var crop = bmp.Clone(rect, PixelFormat.Format32bppArgb))
                crop.Save(output, ImageFormat.Png);
            return string.Format("{0}x{1} -> {2}x{3}", w, h, rect.Width, rect.Height);
        }
    }
}
"@
}
$src = (Resolve-Path -LiteralPath $Source).Path
$dir = Split-Path -Parent $Target
if ($dir) { New-Item -ItemType Directory -Force $dir | Out-Null }
$dst = [IO.Path]::GetFullPath((Join-Path (Get-Location) $Target))
$size = [HaloCleaner]::Clean($src, $dst, $Floor, $Ceiling, $Padding)
"{0}: {1}" -f $Target, $size
