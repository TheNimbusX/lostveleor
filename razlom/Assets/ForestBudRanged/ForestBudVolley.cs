using UnityEngine;

// Совместимость со старыми демонстрационными префабами. Выпуск плодов теперь
// принадлежит Game.Sim; AnimationEvent не создаёт объекты и не наносит урон.
public sealed class ForestBudVolley : MonoBehaviour
{
    private void Awake() => enabled = false;
    public void FireFiveFruits() { }
}