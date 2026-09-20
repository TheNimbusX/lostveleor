<#
    Finds UI elements on a generated transparent sheet and cuts each one out.

    An element is a connected island of pixels with alpha above -Body (the
    painted plate itself; the soft outer glow of the sheet stays below it).
    Each island is cropped with -Pad and its alpha is remapped so the glow
    halo disappears: alpha <= Cut becomes 0, above it is stretched to 0..255.
    Glows are re-added in Unity as separate animated layers.

    Output: <OutDir>/<sheet>-NN.png plus <OutDir>/<sheet>-index.png, a contact
    sheet with the numbers, so the pieces can be named by eye.

    Usage: .\detect-slice.ps1 -Sheet art/sheet-pause-panels.png -OutDir pause-raw
#>
param([string] $Sheet, [string] $OutDir, [int] $Body = 200, [int] $Cut = 110, [int] $Pad = 10, [int] $MinArea = 1500)

Add-Type -AssemblyName System.Drawing
if (-not ('SheetSlicer' -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class SheetSlicer
{
    public static List<Rectangle> Detect(string path, int body, int minArea)
    {
        using (var bmp = new Bitmap(path))
        {
            int w = bmp.Width, h = bmp.Height;
            byte[] px = Read(bmp);
            var label = new bool[w * h];
            var boxes = new List<Rectangle>();
            var stack = new Stack<int>();
            for (int s = 0; s < w * h; s++)
            {
                if (label[s] || px[s * 4 + 3] <= body) continue;
                int minX = w, minY = h, maxX = 0, maxY = 0, area = 0;
                label[s] = true; stack.Push(s);
                while (stack.Count > 0)
                {
                    int p = stack.Pop(); area++;
                    int x = p % w, y = p / w;
                    if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                    if (x > 0) Visit(p - 1, label, px, body, stack);
                    if (x < w - 1) Visit(p + 1, label, px, body, stack);
                    if (y > 0) Visit(p - w, label, px, body, stack);
                    if (y < h - 1) Visit(p + w, label, px, body, stack);
                }
                if (area >= minArea) boxes.Add(Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1));
            }
            boxes.Sort((a, b) => Math.Abs(a.Top - b.Top) > 60 ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left));
            return boxes;
        }
    }

    public static void Cut(string path, Rectangle box, int pad, int cut, string output)
    {
        using (var bmp = new Bitmap(path))
        {
            var r = Rectangle.FromLTRB(Math.Max(0, box.Left - pad), Math.Max(0, box.Top - pad), Math.Min(bmp.Width, box.Right + pad), Math.Min(bmp.Height, box.Bottom + pad));
            using (var piece = bmp.Clone(r, PixelFormat.Format32bppArgb))
            {
                byte[] px = Read(piece);
                for (int i = 3; i < px.Length; i += 4)
                {
                    int a = px[i];
                    px[i] = (byte)(a <= cut ? 0 : Math.Min(255, (a - cut) * 255 / (255 - cut)));
                }
                Write(piece, px);
                piece.Save(output, ImageFormat.Png);
            }
        }
    }

    static void Visit(int p, bool[] label, byte[] px, int body, Stack<int> stack)
    {
        if (label[p] || px[p * 4 + 3] <= body) return;
        label[p] = true; stack.Push(p);
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

$sheetPath = (Resolve-Path $Sheet).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null
$name = [IO.Path]::GetFileNameWithoutExtension($sheetPath) -replace '^sheet-', ''
$boxes = [SheetSlicer]::Detect($sheetPath, $Body, $MinArea)

$index = New-Object Drawing.Bitmap 1344, 760
$g = [Drawing.Graphics]::FromImage($index); $g.Clear([Drawing.Color]::FromArgb(255, 60, 70, 60))
$source = [Drawing.Image]::FromFile($sheetPath); $g.DrawImage($source, 0, 0, 1344, 760); $source.Dispose()
$font = New-Object Drawing.Font 'Segoe UI', 18, ([Drawing.FontStyle]::Bold)
$n = 0
foreach ($box in $boxes) {
    $n++
    $file = Join-Path $OutDir ('{0}-{1:00}.png' -f $name, $n)
    [SheetSlicer]::Cut($sheetPath, $box, $Pad, $Cut, (Resolve-Path $OutDir).Path + '\' + [IO.Path]::GetFileName($file))
    '{0:00}: x={1} y={2} w={3} h={4}' -f $n, $box.X, $box.Y, $box.Width, $box.Height
    $g.DrawRectangle([Drawing.Pens]::Yellow, $box.X / 2, $box.Y / 2, $box.Width / 2, $box.Height / 2)
    $g.DrawString($n.ToString(), $font, [Drawing.Brushes]::Yellow, $box.X / 2 + 4, $box.Y / 2 + 2)
}
$g.Dispose(); $index.Save((Join-Path (Resolve-Path $OutDir).Path "$name-index.png")); $index.Dispose()
