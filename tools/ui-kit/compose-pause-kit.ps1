<#
    Builds Assets/UI/Kit/Pause from the pieces cut by detect-slice.ps1
    (ART/UI/kit-2026-09-15/pause-raw and pause-raw-glow).

    Coral pieces come from sheet-pause-coral: the first coral pieces carried a
    four-diamond emblem that is not Pelag's sign (owner, 15 Sept), they are
    not used. The segmented bar loses its painted dividers (they would stretch
    with 9-slice and fit only three options); the slider fill is rebuilt from
    the painted progress bar with a mirrored rounded end.

    slices.txt: name left bottom right top ppu  (ppu makes the sprite's native
    size match its intended size on the 1080p canvas).
#>
param(
    [string] $Raw = 'ART/UI/kit-2026-09-15/pause-raw',
    [string] $Glow = 'ART/UI/kit-2026-09-15/pause-raw-glow',
    [string] $OutDir = 'razlom/Assets/UI/Kit/Pause'
)

Add-Type -AssemblyName System.Drawing
if (-not ('PauseKitComposer' -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class PauseKitComposer
{
    // Replaces bright vertical divider columns inside the bar with the plate colour next to them.
    public static int RemoveDividers(string input, string output)
    {
        using (var bmp = Load(input))
        {
            int w = bmp.Width, h = bmp.Height;
            byte[] px = Read(bmp);
            int y0 = (int)(h * 0.3), y1 = (int)(h * 0.7);
            var light = new double[w];
            for (int x = 0; x < w; x++)
            {
                double sum = 0;
                for (int y = y0; y < y1; y++) { int i = (y * w + x) * 4; sum += px[i] + px[i + 1] + px[i + 2]; }
                light[x] = sum / (3.0 * (y1 - y0));
            }
            var sorted = (double[])light.Clone(); Array.Sort(sorted);
            double median = sorted[w / 2];
            int fixedColumns = 0;
            // Порог низкий и замена с запасом ±3 колонки: краешки линии светлее
            // фона совсем чуть-чуть и после первой чистки оставались видны.
            var mark = new bool[w];
            for (int x = (int)(w * 0.12); x < (int)(w * 0.88); x++)
                if (light[x] >= median + 10)
                    for (int d = -3; d <= 3; d++) if (x + d >= 0 && x + d < w) mark[x + d] = true;
            for (int x = (int)(w * 0.12); x < (int)(w * 0.88); x++)
            {
                if (!mark[x]) continue;
                int source = x - 18;
                while (source > 0 && mark[source]) source--;
                // На всю высоту: у верхней и нижней кромки оставались засечки от линии.
                for (int y = 0; y < h; y++)
                    for (int c = 0; c < 4; c++) px[(y * w + x) * 4 + c] = px[(y * w + source) * 4 + c];
                fixedColumns++;
            }
            Write(bmp, px);
            bmp.Save(output, ImageFormat.Png);
            return fixedColumns;
        }
    }

    // Cyan part of the painted progress bar with its left rounded end mirrored onto the right.
    public static int BuildFill(string input, string output)
    {
        using (var bmp = Load(input))
        {
            int w = bmp.Width, h = bmp.Height;
            byte[] px = Read(bmp);
            int mid = h / 2, end = w - 1;
            for (int x = w / 4; x < w; x++)
            {
                int i = (mid * w + x) * 4;               // BGRA
                bool cyan = px[i] > 150 && px[i] > px[i + 2] + 60;
                if (!cyan) { end = x; break; }
            }
            int cap = Math.Min(h, end / 3);
            int width = end;
            using (var fill = new Bitmap(width, h, PixelFormat.Format32bppArgb))
            {
                byte[] outPx = new byte[width * h * 4];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int sx = x < width - cap ? x : (width - 1 - x);
                        Array.Copy(px, (y * w + sx) * 4, outPx, (y * width + x) * 4, 4);
                    }
                Write(fill, outPx);
                fill.Save(output, ImageFormat.Png);
            }
            return end;
        }
    }

    static Bitmap Load(string path)
    {
        using (var loaded = new Bitmap(path)) return loaded.Clone(new Rectangle(0, 0, loaded.Width, loaded.Height), PixelFormat.Format32bppArgb);
    }

    static byte[] Read(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[bmp.Width * bmp.Height * 4];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        bmp.UnlockBits(data);
        return bytes;
    }

    static void Write(Bitmap bmp, byte[] bytes)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        bmp.UnlockBits(data);
    }
}
"@
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$out = (Resolve-Path $OutDir).Path

# name, source, left, bottom, right, top, ppu
$pieces = @(
    @('pause_panel',               "$Raw/pause-panels-01.png",   110, 110, 110, 110, 202),
    @('pause_row',                 "$Raw/pause-panels-02.png",    90,  70,  70,  70, 235),
    @('pause_row_hover',           "$Raw/pause-panels-03.png",    90,  80,  80,  80, 235),
    @('pause_divider',             "$Raw/pause-panels-04.png",     0,   0,   0,   0, 580),
    @('pause_tile',                "$Raw/pause-panels-05.png",    90,  90,  90,  90, 200),
    @('pause_button',              "$Raw/pause-buttons-04.png",  100,  70, 100,  70, 220),
    @('pause_button_hover',        "$Raw/pause-buttons-05.png",  100,  75, 100,  75, 220),
    @('pause_button_pressed',      "$Raw/pause-buttons-06.png",  100,  70, 100,  70, 220),
    @('pause_tab_off',             "$Raw/pause-buttons-08.png",   90,  60,  90,  60, 280),
    @('pause_tab_hover',           "$Raw/pause-buttons-09.png",   90,  65,  90,  65, 280),
    @('pause_glow_ring',           "$Glow/pause-buttons-10.png", 170, 130, 170, 130, 220),
    @('pause_button_coral',        "$Raw/pause-coral-01.png",    100,  70, 100,  70, 217),
    @('pause_button_coral_hover',  "$Raw/pause-coral-02.png",    100,  70, 100,  70, 217),
    @('pause_button_coral_pressed',"$Raw/pause-coral-03.png",    100,  70, 100,  70, 217),
    @('pause_tab_on',              "$Raw/pause-coral-04.png",     90,  60,  90,  60, 287),
    @('pause_segment_on',          "$Raw/pause-coral-05.png",     80,  55,  80,  55, 350),
    @('pause_ribbon',              "$Raw/pause-coral-06.png",    330,  90, 330,  90, 308),
    # Шапка с объёмом (16 сентября): передняя лента с загнутыми концами поверх панели, хвосты — за ней.
    @('pause_header',              "$Raw/pause-header-b-01.png", 250,  80, 250,  80, 336),
    @('pause_header_tail_left',    "$Raw/pause-header-b-02.png",   0,   0,   0,   0, 336),
    @('pause_header_tail_right',   "$Raw/pause-header-b-03.png",   0,   0,   0,   0, 336),
    @('pause_dropdown',            "$Raw/pause-controls-03.png",  90,  60, 180,  60, 316),
    @('pause_toggle_on',           "$Raw/pause-controls-04.png",   0,   0,   0,   0, 342),
    @('pause_toggle_off',          "$Raw/pause-controls-05.png",   0,   0,   0,   0, 342),
    @('pause_slider_track',        "$Raw/pause-controls-06.png",  40,  36,  40,  36, 282),
    @('pause_knob',                "$Raw/pause-controls-08.png",   0,   0,   0,   0, 370),
    @('pause_keycap',              "$Raw/pause-controls-10.png",  90,  70,  90,  70, 386),
    @('pause_keycap_glow',         "$Glow/pause-controls-11.png", 150, 130, 150, 130, 386)
)

$lines = @()
foreach ($p in $pieces) {
    Copy-Item $p[1] (Join-Path $out ($p[0] + '.png')) -Force
    $lines += '{0} {1} {2} {3} {4} {5}' -f $p[0], $p[2], $p[3], $p[4], $p[5], $p[6]
}

$columns = [PauseKitComposer]::RemoveDividers((Resolve-Path "$Raw/pause-controls-01.png").Path, (Join-Path $out 'pause_segmented.png'))
"segmented: {0} divider columns cleaned" -f $columns
$lines += 'pause_segmented 90 60 90 60 307'

$end = [PauseKitComposer]::BuildFill((Resolve-Path "$Raw/pause-controls-07.png").Path, (Join-Path $out 'pause_slider_fill.png'))
"slider fill: cyan ends at x={0}" -f $end
$lines += 'pause_slider_fill 40 38 40 38 540'

[IO.File]::WriteAllLines((Join-Path $out 'slices.txt'), [string[]]$lines)
"pause kit: {0} sprites -> {1}" -f $lines.Count, $out
