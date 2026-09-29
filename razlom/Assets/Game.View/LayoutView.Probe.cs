using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    // Отпечаток собранной арены для съёмочного плеера (-capture-layout-hash): маски земли, сетки,
    // расстановка декора, трава и листья — по хешу на каждую часть, строкой «[layout-hash]» в журнале.
    // Им сверяется, что сборка по кадрам и на рабочих потоках (поток T1, 29.09) даёт ровно ту же
    // арену, что прежняя сборка одним кадром: тот же сид — те же числа до бита.
    public sealed partial class LayoutView
    {
        private static readonly bool HashProbe =
            Array.IndexOf(Environment.GetCommandLineArgs(), "-razlom-capture") >= 0
            && Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-layout-hash") >= 0;

        private struct Fnv
        {
            public ulong Value;
            public static Fnv Start() => new Fnv { Value = 14695981039346656037UL };
            public void Add(uint word)
            {
                unchecked
                {
                    for (int b = 0; b < 4; b++) { Value ^= (byte)(word >> (b * 8)); Value *= 1099511628211UL; }
                }
            }
            public void Add(int value) => Add(unchecked((uint)value));
            public void Add(float value) => Add(BitConverter.SingleToInt32Bits(value));
            public void Add(bool value) => Add(value ? 1 : 0);
            public void Add(Vector2 v) { Add(v.x); Add(v.y); }
            public void Add(Vector3 v) { Add(v.x); Add(v.y); Add(v.z); }
            public void Add(Vector4 v) { Add(v.x); Add(v.y); Add(v.z); Add(v.w); }
            public void Add(Quaternion q) { Add(q.x); Add(q.y); Add(q.z); Add(q.w); }
            public void Add(Matrix4x4 m) { for (int i = 0; i < 16; i++) Add(m[i]); }
            public void Add(string text) { if (text == null) { Add(-1); return; } foreach (char c in text) Add((int)c); }
            public void Add(byte[] data)
            {
                if (data == null) { Add(-1); return; }
                unchecked { foreach (byte b in data) { Value ^= b; Value *= 1099511628211UL; } }
            }
            public void Add(float[] data) { if (data == null) { Add(-1); return; } foreach (float f in data) Add(f); }
            public void Add(Color32[] data)
            {
                if (data == null) { Add(-1); return; }
                foreach (var c in data) Add(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));
            }
            public void Add(Mesh mesh)
            {
                if (mesh == null) { Add(-1); return; }
                foreach (var v in mesh.vertices) Add(v);
                foreach (var n in mesh.normals) Add(n);
                foreach (var u in mesh.uv) Add(u);
                foreach (int t in mesh.triangles) Add(t);
            }
            public override string ToString() => Value.ToString("x16");
        }

        private void DumpLayoutHash()
        {
            if (!HashProbe || _shownMap == null) return;
            var masks = Fnv.Start();
            masks.Add(_trailBounds); masks.Add(_trailPixels); masks.Add(_clearingPixels); masks.Add(_wearPixels);
            var surface = Fnv.Start();
            surface.Add(_campSurfacePixels); surface.Add(_forestDistance); surface.Add(_clearingDistance);
            var meshes = Fnv.Start();
            meshes.Add(_backgroundMesh); meshes.Add(_waterMesh); meshes.Add(_shoreMesh);
            meshes.Add(_riverMesh); meshes.Add(_riverBankMesh); meshes.Add(_bankMesh);
            foreach (var mesh in _outlineMeshes) meshes.Add(mesh);
            var decor = Fnv.Start();
            var looks = Fnv.Start();
            decor.Add(_decorCount);
            for (int i = 0; i < _decorCount; i++)
            {
                var item = _decor[i];
                decor.Add(_decorVariant[i]);
                decor.Add(item.position); decor.Add(item.rotation); decor.Add(item.localScale);
                decor.Add(item.gameObject.activeSelf);
                foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials) looks.Add(material != null ? material.name : null);
            }
            var tiles = Fnv.Start();
            tiles.Add(_tileCount);
            for (int i = 0; i < _tileCount; i++)
            {
                tiles.Add(_tiles[i].position); tiles.Add(_tiles[i].localScale); tiles.Add(_tiles[i].gameObject.activeSelf);
            }
            for (int i = 0; i < _solids.Count; i++)
            {
                tiles.Add(_solids[i].transform.position); tiles.Add(_solids[i].transform.rotation);
                tiles.Add(_solids[i].transform.GetChild(0).localScale); tiles.Add(_solids[i].transform.GetChild(0).localPosition);
            }
            foreach (var portal in _portals) { tiles.Add(portal.position); tiles.Add(portal.rotation); }
            foreach (var cache in _caches) tiles.Add(cache.position);
            var grass = Fnv.Start();
            grass.Add(_grassField.Count);
            foreach (var m in _grassField) grass.Add(m);
            foreach (var list in _leafField)
            {
                if (list == null) { grass.Add(-1); continue; }
                grass.Add(list.Count);
                foreach (var m in list) grass.Add(m);
            }
            var extra = Fnv.Start();
            foreach (var pond in _ponds) extra.Add(pond);
            foreach (var spot in _landmarkSpots) extra.Add(spot);
            extra.Add(_reliefOffset);
            extra.Add(_fogPixels);
            if (_wisps != null) { extra.Add(_wisps.transform.position); extra.Add(_wisps.shape.radius); extra.Add(_wisps.shape.scale); }
            Debug.Log($"[layout-hash] глубина {(_driver != null && _driver.Run != null ? _driver.Run.Depth : -1)} сид {_layoutSeed}: "
                      + $"маски {masks} земля {surface} сетки {meshes} декор {decor} ({_decorCount}) материалы {looks} "
                      + $"плиты {tiles} трава {grass} ({_grassField.Count}) прочее {extra}");
        }
    }
}
