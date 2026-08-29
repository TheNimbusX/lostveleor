using UnityEngine;
using UnityEngine.Events;

// Presentation-only animation cues. Gameplay hit confirmation and damage stay in Game.Sim.
[DisallowMultipleComponent]
public sealed class OrvillAnimationCueRelay : MonoBehaviour
{
    [SerializeField] private UnityEvent onAttackContact = new UnityEvent();
    [SerializeField] private UnityEvent onShieldImpact = new UnityEvent();
    [SerializeField] private UnityEvent onGuardBreak = new UnityEvent();
    [SerializeField] private UnityEvent onDeathImpact = new UnityEvent();

    public void AttackContactCue() => onAttackContact.Invoke();
    public void ShieldImpactCue() => onShieldImpact.Invoke();
    public void GuardBreakCue() => onGuardBreak.Invoke();
    public void DeathImpactCue() => onDeathImpact.Invoke();
}
