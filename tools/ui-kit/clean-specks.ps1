<#
    Removes stray specks from sliced kit sprites (tools/ui-kit/slice-art.ps1
    leaves tiny detached islands, e.g. above menu_exit).

    A speck is a connected island of visible pixels (4-neighbour, alpha > 8)
    smaller than KeepRatio of the largest island in the same image. Separate
    real parts (sound waves, arrow next to the door) are far bigger and stay.

    Usage: .\clean-specks.ps1 -Paths a.png, b.png [-KeepRatio 0.01]
#>
param([string[]] $Paths, [double] $KeepRatio = 0.01, [switch] $Trim)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class SpeckCleaner
{
    public static int Clean(string path, double keepRatio, bool forceTrim)
    {
        Bitmap source;
        using (var loaded = new Bitmap(path)) source = loaded.Clone(new Rectangle(0, 0, loaded.Width, loaded.Height), PixelFormat.Format32bppArgb);
        int w = source.Width, h = source.Height;
        var data = source.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var bytes = new byte[w * h * 4];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

        var label = new int[w * h];
        var areas = new List<int> { 0 };
        var stack = new Stack<int>();
        for (int start = 0; start < w * h; start++)
        {
            if (label[start] != 0 || bytes[start * 4 + 3] <= 8) continue;
            int id = areas.Count, area = 0;
            label[start] = id; stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop(); area++;
                int x = p % w, y = p / w;
                if (x > 0) Visit(p - 1, id, label, bytes, stack);
                if (x < w - 1) Visit(p + 1, id, label, bytes, stack);
                if (y > 0) Visit(p - w, id, label, bytes, stack);
                if (y < h - 1) Visit(p + w, id, label, bytes, stack);
            }
            areas.Add(area);
        }

        int largest = 0;
        foreach (int a in areas) largest = Math.Max(largest, a);
        int removed = 0;
        for (int p = 0; p < w * h; p++)
        {
            int id = label[p];
            if (id == 0 || areas[id] >= largest * keepRatio) continue;
            bytes[p * 4] = bytes[p * 4 + 1] = bytes[p * 4 + 2] = bytes[p * 4 + 3] = 0;
            removed++;
        }
        Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        source.UnlockBits(data);
        if (removed > 0 || forceTrim)
        {
            // Trim the transparent margin the speck left behind: a sprite with an empty band
            // is drawn smaller and off-centre by preserveAspect next to its siblings.
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int p = 0; p < w * h; p++)
            {
                if (bytes[p * 4 + 3] <= 8) continue;
                int x = p % w, y = p / w;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            if (maxX >= minX)
            {
                const int pad = 6;
                var crop = Rectangle.FromLTRB(Math.Max(0, minX - pad), Math.Max(0, minY - pad), Math.Min(w, maxX + 1 + pad), Math.Min(h, maxY + 1 + pad));
                Bitmap trimmed = source.Clone(crop, PixelFormat.Format32bppArgb);
                source.Dispose();
                source = trimmed;
            }
            // GDI+ refuses to overwrite the file a clone was made from: write beside it, then replace.
            string temp = path + ".clean.tmp";
            source.Save(temp, ImageFormat.Png);
            source.Dispose();
            System.IO.File.Copy(temp, path, true);
            System.IO.File.Delete(temp);
            return removed;
        }
        source.Dispose();
        return removed;
    }

    static void Visit(int p, int id, int[] label, byte[] bytes, Stack<int> stack)
    {
        if (label[p] != 0 || bytes[p * 4 + 3] <= 8) return;
        label[p] = id; stack.Push(p);
    }
}
"@

foreach ($path in $Paths) {
    $full = (Resolve-Path $path).Path
    $removed = [SpeckCleaner]::Clean($full, $KeepRatio, $Trim.IsPresent)
    "{0}: removed {1} px" -f $path, $removed
}
