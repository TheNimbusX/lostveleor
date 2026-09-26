using TMPro;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Текст «Дыма и света» проявляется по буквам слева направо: каждая буква всплывает на
    /// пару единиц снизу и набирает яркость. Ведёт <see cref="UiInkGroup"/> через <see cref="Hidden"/>.
    ///
    /// Правка — в OnPreRenderText, то есть при каждой пересборке сетки TMP: если текст меняется
    /// посреди появления (числа, секунды), буквы не вспыхивают целиком. В покое (Hidden = 0)
    /// правка ничего не делает.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    [DisallowMultipleComponent]
    public sealed class UiInkText : MonoBehaviour
    {
        [Tooltip("Задержка в группе, с — текст встаёт после дыма")] public float Delay = .12f;
        [Tooltip("Доля строки, которую занимает проявление одной буквы")] [Range(.05f, 1f)] public float Softness = .35f;
        [Tooltip("Откуда всплывает буква, единицы Canvas")] public float Rise = 6f;

        TMP_Text _text;
        float _hidden;

        public float Hidden
        {
            get => _hidden;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, _hidden)) return;
                _hidden = value;
                if (_text == null) _text = GetComponent<TMP_Text>();
                // Только «свойства изменились» заставляет TMP пересобрать сетку и позвать OnPreRenderText;
                // SetVerticesDirty без него рисует старую сетку, и буквы застревали невидимыми.
                if (_text != null) _text.havePropertiesChanged = true;
            }
        }

        void OnEnable()
        {
            _text = GetComponent<TMP_Text>();
            _text.OnPreRenderText += Modify;
            _text.havePropertiesChanged = true;
        }

        void OnDisable()
        {
            if (_text != null) _text.OnPreRenderText -= Modify;
        }

        void Modify(TMP_TextInfo info)
        {
            if (_hidden <= 0f) return;
            int count = info.characterCount;
            float progress = 1f - _hidden;
            float span = 1f - Softness;
            for (int i = 0; i < count; i++)
            {
                TMP_CharacterInfo c = info.characterInfo[i];
                if (!c.isVisible) continue;
                float at = count > 1 ? (float)i / (count - 1) : 0f;
                float k = Mathf.Clamp01((progress - at * span) / Softness);
                k = k * k * (3f - 2f * k);
                int mesh = c.materialReferenceIndex, v = c.vertexIndex;
                Color32[] colors = info.meshInfo[mesh].colors32;
                Vector3[] vertices = info.meshInfo[mesh].vertices;
                var lift = new Vector3(0f, -Rise * (1f - k), 0f);
                for (int j = 0; j < 4; j++)
                {
                    colors[v + j].a = (byte)(colors[v + j].a * k);
                    vertices[v + j] += lift;
                }
            }
        }
    }
}
