#!/bin/sh
# usage: bl.sh file.blend script.py args...   (runs headless Blender, filters noise)
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
blend="$1"; shift; script="$1"; shift
"$B" -b "$blend" -P "$script" -- "$@" 2>&1 | grep -v -E "^Fra:|a_Rodin|unregister|mod.unregister|~~~|\^\^\^|load_post.remove|^Blender quit|^Blender 5|^\s*$|^  File .*bpy.utils"
