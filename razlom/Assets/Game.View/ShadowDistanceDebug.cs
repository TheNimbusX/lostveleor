using UnityEngine;

/// <summary>
/// Бывший отладчик дистанции теней. Компонент висит в SampleScene, поэтому
/// класс оставлен, но пустой: он каждый кадр читал ассет конвейера и писал
/// предупреждения со стеком в лог. Выключается при загрузке.
/// </summary>
public class ShadowDistanceDebug : MonoBehaviour
{
    private void Awake() => enabled = false;
}
