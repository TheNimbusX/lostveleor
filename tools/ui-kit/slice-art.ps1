<#
.SYNOPSIS
    Режет сгенерированные листы UI-пака на отдельные спрайты.

.DESCRIPTION
    Генератор на прозрачном фоне оставляет вокруг каждой иконки широкий
    полупрозрачный ореол. В игре он читается как грязное пятно, поэтому
    слабая альфа срезается: всё, что прозрачнее порога, пропадает, а край
    сохраняет сглаживание за счёт перерасчёта оставшейся альфы.
    Свечение рамок выделения, наоборот, нужно — у них порог не применяется.
#>
param([string] $KitDir = '')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if ($KitDir -eq '') { $KitDir = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'ART\UI\kit-2026-09-15' }

if (-not ('UiKitSlicer' -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class UiKitSlicer
{
    // Вырезает область, срезает ореол и обрезает пустые поля.
    public static Bitmap Cut(Bitmap sheet, Rectangle area, int alphaCut, int pad)
    {
        var crop = sheet.Clone(area, PixelFormat.Format32bppArgb);
        var data = crop.LockBits(new Rectangle(0, 0, crop.Width, crop.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int stride = data.Stride, w = crop.Width, h = crop.Height;
        var px = new byte[stride * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * stride + x * 4;
            int a = px[i + 3];
            if (alphaCut > 0)
            {
                a = a <= alphaCut ? 0 : (a - alphaCut) * 255 / (255 - alphaCut);
                px[i + 3] = (byte)a;
            }
            if (a > 8) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
        }
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        crop.UnlockBits(data);
        if (maxX < 0) return crop;
        minX = Math.Max(0, minX - pad); minY = Math.Max(0, minY - pad);
        maxX = Math.Min(w - 1, maxX + pad); maxY = Math.Min(h - 1, maxY + pad);
        var trimmed = crop.Clone(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1), PixelFormat.Format32bppArgb);
        crop.Dispose();
        return trimmed;
    }
}
'@
}

function Slice-Grid([string]$sheetName, [string]$outFolder, [string[]]$names, [int]$cols, [int]$alphaCut) {
    $out = Join-Path $KitDir $outFolder; [IO.Directory]::CreateDirectory($out) | Out-Null
    $sheet = [Drawing.Bitmap]::FromFile((Join-Path $KitDir "art\$sheetName"))
    $cw = [int]($sheet.Width / $cols); $rows = [int][math]::Ceiling($names.Count / $cols); $ch = [int]($sheet.Height / $rows)
    for ($i = 0; $i -lt $names.Count; $i++) {
        $rect = [Drawing.Rectangle]::new(($i % $cols) * $cw, [math]::Floor($i / $cols) * $ch, $cw, $ch)
        $bmp = [UiKitSlicer]::Cut($sheet, $rect, $alphaCut, 6)
        $bmp.Save((Join-Path $out ($names[$i] + '.png')), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    }
    $sheet.Dispose()
    "$sheetName -> $($names.Count) спрайтов в $outFolder"
}

function Slice-Boxes([string]$sheetName, [string]$outFolder, [object[]]$boxes) {
    $out = Join-Path $KitDir $outFolder; [IO.Directory]::CreateDirectory($out) | Out-Null
    $sheet = [Drawing.Bitmap]::FromFile((Join-Path $KitDir "art\$sheetName"))
    foreach ($b in $boxes) {
        $rect = [Drawing.Rectangle]::new($b[1], $b[2], $b[3] - $b[1], $b[4] - $b[2])
        $bmp = [UiKitSlicer]::Cut($sheet, $rect, $b[5], 4)
        $bmp.Save((Join-Path $out ($b[0] + '.png')), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    }
    $sheet.Dispose()
    "$sheetName -> $($boxes.Count) спрайтов в $outFolder"
}

Slice-Grid 'sheet-icons-game.png' 'icons' @(
    'coin', 'shard', 'lavidium', 'heart',
    'stat_damage', 'stat_armor', 'stat_attack_speed', 'stat_crit',
    'stat_cooldown', 'stat_radius', 'stat_range', 'stat_duration',
    'lock', 'npc_smith', 'npc_trader', 'place_camp') 4 150

Slice-Grid 'sheet-icons-menu.png' 'icons' @(
    'menu_play', 'menu_settings', 'menu_controls', 'menu_camp',
    'menu_exit', 'menu_reset', 'menu_apply', 'menu_close',
    'menu_screen', 'menu_shadows', 'menu_framerate', 'menu_ui_scale',
    'menu_sound', 'menu_music', 'menu_level_up', 'menu_reforge') 4 150

# Координаты в пикселях листа 2048×1360. Последнее число — порог ореола.
Slice-Boxes 'sheet-ribbons.png' 'ornaments' @(
    @('ribbon_long', 70, 70, 1965, 385, 150),
    @('ribbon_label', 355, 400, 1175, 585, 150),
    @('pennant', 1355, 330, 1700, 685, 150),
    @('ornament_diamond', 335, 670, 485, 825, 150),
    @('ornament_divider', 555, 700, 1155, 785, 150),
    @('ornament_diamond_small', 1225, 690, 1345, 805, 150),
    @('ornament_sparkle', 1425, 690, 1545, 815, 150),
    @('glow_frame_cyan', 135, 895, 975, 1255, 0),
    @('glow_frame_coral', 1065, 895, 1895, 1255, 0))
