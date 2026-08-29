using UnityEngine;
using UnityEngine.Events;

// Presentation-only animation cues. Gameplay hit confirmation and damage stay in Game.Sim.
[DisallowMultipleComponent]
public sealed class PelagAnimationCueRelay : MonoBehaviour
{
    [SerializeField] private UnityEvent onAttackContact = new UnityEvent();
    [SerializeField] private UnityEvent onHeavyImpact = new UnityEvent();
    [SerializeField] private UnityEvent onHookRelease = new UnityEvent();
    [SerializeField] private UnityEvent onHookRecover = new UnityEvent();
    [SerializeField] private UnityEvent onDeathImpact = new UnityEvent();

    public void AttackContactCue() => onAttackContact.Invoke();
    public void HeavyImpactCue() => onHeavyImpact.Invoke();
    public void HookReleaseCue() => onHookRelease.Invoke();
    public void HookRecoverCue() => onHookRecover.Invoke();
    public void DeathImpactCue() => onDeathImpact.Invoke();
}
