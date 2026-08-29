using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Пул игровых объектов одной формы. Всё создаётся на прогреве, в бою —
    /// только Acquire/Release.
    ///
    /// В гриндилке за минуту рождаются и умирают тысячи снарядов, цифр и трупов;
    /// Instantiate на каждом — это кадровые провалы и работа сборщику мусора.
    /// Пул заводится сразу, даже там, где сущности пока не досоздаются: привычка
    /// дешевле, чем переделка горячего пути потом.
    /// </summary>
    public sealed class ViewPool
    {
        private readonly Transform _parent;
        private readonly System.Func<GameObject> _factory;

        private GameObject[] _items;
        private int[] _freeStack;
        private int _freeCount;
        private int _created;

        private bool _overflowReported;

        public int Created => _created;

        public ViewPool(Transform parent, System.Func<GameObject> factory, int prewarm)
        {
            _parent = parent;
            _factory = factory;

            int capacity = Mathf.Max(1, prewarm);
            _items = new GameObject[capacity];
            _freeStack = new int[capacity];

            for (int i = 0; i < capacity; i++) Grow();
        }

        private int Grow()
        {
            if (_created == _items.Length)
            {
                System.Array.Resize(ref _items, _items.Length * 2);
                System.Array.Resize(ref _freeStack, _freeStack.Length * 2);
            }

            int slot = _created++;
            GameObject go = _factory();
            go.transform.SetParent(_parent, false);
            go.SetActive(false);
            go.AddComponent<PoolTag>().Slot = slot;
            _items[slot] = go;
            _freeStack[_freeCount++] = slot;
            return slot;
        }

        /// <summary>
        /// Достаёт объект из пула. Если свободных не осталось — пул расширяется,
        /// но об этом сообщается один раз: расширение в бою и есть тот самый
        /// Instantiate, которого мы избегаем, и прогрев надо увеличить.
        /// </summary>
        public GameObject Acquire()
        {
            if (_freeCount == 0)
            {
                if (!_overflowReported)
                {
                    _overflowReported = true;
                    Debug.LogWarning($"[Разлом] Пул исчерпан на {_created} объектах — увеличь прогрев.");
                }
                Grow();
            }

            int slot = _freeStack[--_freeCount];
            GameObject go = _items[slot];
            go.GetComponent<PoolTag>().Free = false;
            go.SetActive(true);
            return go;
        }

        /// <summary>
        /// Возврат в пул. Слот хранится на самом объекте, чтобы возврат стоил
        /// столько же при пуле в десять объектов и в десять тысяч.
        /// </summary>
        public void Release(GameObject go)
        {
            if (go == null) return;

            PoolTag tag = go.GetComponent<PoolTag>();
            if (tag == null || tag.Slot < 0 || tag.Slot >= _created) return;
            if (!ReferenceEquals(_items[tag.Slot], go)) return;

            // Защита от двойного возврата: он задвоил бы слот в стеке, и один
            // объект выдался бы двум владельцам сразу. Флаг, а не activeSelf:
            // спрятанный объект (например, труп) всё ещё занят.
            if (tag.Free) return;

            tag.Free = true;
            go.SetActive(false);
            _freeStack[_freeCount++] = tag.Slot;
        }
    }

    /// <summary>Номер слота в пуле. Висит на объекте, чтобы возврат был за O(1).</summary>
    public sealed class PoolTag : MonoBehaviour
    {
        public int Slot = -1;
        public bool Free = true;
    }
}
