using System.IO;

namespace Game.Tests
{
    /// <summary>
    /// Файл старого формата (версии 1–9). С 06.10 такие сохранения не читаются вовсе:
    /// кодек обязан узнать их по заголовку и сообщить «устарело», а не разбирать тело.
    /// Поэтому фикстуре достаточно заголовка, произвольного тела и верной контрольной
    /// суммы — так проверка не зависит от того, как именно выглядел каждый старый формат.
    /// </summary>
    internal static class LegacyCampSaveFixture
    {
        internal static byte[] Header(int version, int bodyBytes = 24)
        {
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                w.Write(0x43575254); w.Write(version);
                for (int i = 0; i < bodyBytes; i++) w.Write((byte)(i * 7 + version));
                w.Flush(); var payload = stream.ToArray(); uint checksum = 2166136261;
                foreach (byte value in payload) { checksum ^= value; checksum = unchecked(checksum * 16777619); }
                w.Write(checksum); w.Flush(); return stream.ToArray();
            }
        }
    }
}
