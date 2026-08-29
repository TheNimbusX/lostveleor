using UnityEngine;

public enum PelagEquipmentMode
{
    Keep,
    Saber,
    Hook,
    Sheathed
}

public sealed class PelagEquipmentStateBehaviour : StateMachineBehaviour
{
    public PelagEquipmentMode onEnter = PelagEquipmentMode.Keep;
    public PelagEquipmentMode onExit = PelagEquipmentMode.Keep;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        Apply(animator, onEnter);
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        Apply(animator, onExit);
    }

    private static void Apply(Animator animator, PelagEquipmentMode mode)
    {
        if (mode == PelagEquipmentMode.Keep) return;
        var switcher = animator.GetComponent<RazlomEquipmentSwitcher>();
        if (switcher == null) return;
        switch (mode)
        {
            case PelagEquipmentMode.Saber: switcher.EquipSaber(); break;
            case PelagEquipmentMode.Hook: switcher.EquipHook(); break;
            case PelagEquipmentMode.Sheathed: switcher.SheathAll(); break;
        }
    }
}
