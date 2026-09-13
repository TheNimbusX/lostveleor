using UnityEngine;

/// <summary>Сериализованная маска нужна Undo: пиксели GPU-текстуры сами историю не хранят.</summary>
public sealed class CampPathPaintData : ScriptableObject
{
    public Texture2D Surface;
    public Texture2D ClearedFoliage;
    public int Width;
    public int Height;
    public byte[] OriginalPath;
    public byte[] Path;
}
