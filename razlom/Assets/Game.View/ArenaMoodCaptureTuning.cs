using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Подбор света арены в изолированной съёмке без пересборки плеера: ключ
    /// <c>-capture-mood-set "Mist.HazeDensity=.8;Dusk.SunIntensityScale=.5;Fx.MistHazeTone=.5;Profile.SpotOuterField=1.3"</c>
    /// — поля пресетов (Day / Mist / Dusk), акцента босса (Boss), самого профиля (Profile) и эффектов (Fx) на один
    /// запуск. Части — через «;» (без пробелов: Start-Process в PS 5.1 режет аргумент по пробелам), цвет —
    /// <c>#RRGGBB</c> или <c>r,g,b</c>, вектор — <c>x,y[,z[,w]]</c>, таблица оси — <c>0,0,.15,…</c>.
    /// Только под <c>-razlom-capture</c>: обычная игра и редактор ключ не читают, ассет на диске не меняется.
    /// </summary>
    internal static class ArenaMoodCaptureTuning
    {
        public const string Flag = "-capture-mood-set";

        private static bool _read;
        private static string _value;

        private static string Value
        {
            get
            {
                if (_read) return _value;
                _read = true;
                string[] args = Environment.GetCommandLineArgs();
                if (Array.IndexOf(args, "-razlom-capture") < 0) return _value = null;
                int at = Array.IndexOf(args, Flag);
                _value = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
                return _value;
            }
        }

        /// <summary>Пресеты, акцент босса и поля профиля. Профиль грузится один раз на локацию.</summary>
        public static void ApplyTo(ArenaMoodProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(Value)) return;
            var applied = new StringBuilder();
            foreach (string part in Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Split(part, out string section, out string name, out string raw)) continue;
                object target;
                switch (section.ToLowerInvariant())
                {
                    case "day": target = profile.Day; break;
                    case "mist": target = profile.Mist; break;
                    case "dusk": target = profile.Dusk; break;
                    case "boss": target = profile.BossAccent; break;
                    case "profile": target = profile; break;
                    default: continue;
                }
                if (Set(target, name, raw)) applied.Append(section).Append('.').Append(name).Append('=').Append(raw).Append(' ');
            }
            if (applied.Length > 0) Debug.Log("[arena-mood] ручки на запуск: " + applied.ToString().Trim());
        }

        /// <summary>Ручки эффектов (поля ArenaMoodFx: тон дымки, ореол фонарей…).</summary>
        public static void ApplyTo(ArenaMoodFx fx)
        {
            if (fx == null || string.IsNullOrEmpty(Value)) return;
            var applied = new StringBuilder();
            foreach (string part in Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Split(part, out string section, out string name, out string raw)) continue;
                if (!section.Equals("fx", StringComparison.OrdinalIgnoreCase)) continue;
                if (Set(fx, name, raw)) applied.Append("Fx.").Append(name).Append('=').Append(raw).Append(' ');
            }
            if (applied.Length > 0) Debug.Log("[arena-mood] ручки эффектов на запуск: " + applied.ToString().Trim());
        }

        private static bool Split(string part, out string section, out string name, out string raw)
        {
            section = name = raw = null;
            int eq = part.IndexOf('=');
            int dot = part.IndexOf('.');
            if (eq <= 0 || dot <= 0 || dot > eq) return false;
            section = part.Substring(0, dot).Trim();
            name = part.Substring(dot + 1, eq - dot - 1).Trim();
            raw = part.Substring(eq + 1).Trim();
            return section.Length > 0 && name.Length > 0 && raw.Length > 0;
        }

        private static bool Set(object target, string name, string raw)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field == null)
            {
                Debug.LogWarning("[arena-mood] нет ручки " + target.GetType().Name + "." + name);
                return false;
            }
            object value = Parse(field.FieldType, raw);
            if (value == null)
            {
                Debug.LogWarning("[arena-mood] не разобрать " + name + "=" + raw);
                return false;
            }
            field.SetValue(target, value);
            return true;
        }

        private static object Parse(Type type, string raw)
        {
            if (type == typeof(float)) return TryFloat(raw, out float f) ? (object)f : null;
            if (type == typeof(int))
                return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? (object)i : null;
            if (type == typeof(bool))
                return raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
            if (type == typeof(Color))
            {
                if (raw.StartsWith("#") && ColorUtility.TryParseHtmlString(raw, out Color hex)) return hex;
                float[] c = Floats(raw);
                if (c == null || c.Length < 3) return null;
                return new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1f);
            }
            if (type == typeof(Vector4))
            {
                float[] v = Floats(raw);
                return v != null && v.Length == 4 ? (object)new Vector4(v[0], v[1], v[2], v[3]) : null;
            }
            if (type == typeof(Vector3))
            {
                float[] v = Floats(raw);
                return v != null && v.Length == 3 ? (object)new Vector3(v[0], v[1], v[2]) : null;
            }
            if (type == typeof(Vector2))
            {
                float[] v = Floats(raw);
                return v != null && v.Length == 2 ? (object)new Vector2(v[0], v[1]) : null;
            }
            if (type == typeof(float[])) return Floats(raw);
            return null;
        }

        private static float[] Floats(string raw)
        {
            string[] parts = raw.Split(',');
            var result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!TryFloat(parts[i], out result[i])) return null;
            return result;
        }

        private static bool TryFloat(string raw, out float value) =>
            float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
