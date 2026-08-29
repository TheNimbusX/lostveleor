using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class RazlomEquipmentSwitcher : MonoBehaviour
{
    [Serializable]
    public class SwitchableProp
    {
        public string propName;
        public string storedSocket;
        public string equippedSocket;
        public Vector3 storedLocalPosition;
        public Vector3 storedLocalEuler;
        public Vector3 equippedLocalPosition;
        public Vector3 equippedLocalEuler;
        [NonSerialized] public Transform prop;
        [NonSerialized] public Transform stored;
        [NonSerialized] public Transform equipped;
    }

    [SerializeField] private SwitchableProp[] props = Array.Empty<SwitchableProp>();
    [SerializeField] private bool equipSaberOnAwake = true;

    public int ConfiguredPropCount => props?.Length ?? 0;
    public SwitchableProp[] ConfiguredProps => props;

    private void Awake()
    {
        ResolveReferences();
        if (equipSaberOnAwake) EquipSaber();
    }

    public void Configure(SwitchableProp[] entries)
    {
        props = entries ?? Array.Empty<SwitchableProp>();
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        foreach (var entry in props ?? Array.Empty<SwitchableProp>())
        {
            entry.prop = Find(entry.propName);
            entry.stored = Find(entry.storedSocket);
            entry.equipped = Find(entry.equippedSocket);
        }
    }

    public void SetEquipped(string propName, bool equipped)
    {
        var entry = props.FirstOrDefault(p => p.propName == propName);
        if (entry == null || entry.prop == null) return;
        var socket = equipped ? entry.equipped : entry.stored;
        if (socket == null) return;
        entry.prop.SetParent(socket, false);
        entry.prop.localPosition = equipped ? entry.equippedLocalPosition : entry.storedLocalPosition;
        entry.prop.localRotation = Quaternion.Euler(equipped ? entry.equippedLocalEuler : entry.storedLocalEuler);
    }

    public void SetHandPose(string rendererName, string pose, float value)
    {
        var renderer = GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(r => r.name == rendererName);
        if (renderer == null || renderer.sharedMesh == null) return;
        for (var i=0; i<renderer.sharedMesh.blendShapeCount; i++)
        {
            var name = renderer.sharedMesh.GetBlendShapeName(i);
            if (name.EndsWith(pose, StringComparison.OrdinalIgnoreCase))
                renderer.SetBlendShapeWeight(i, Mathf.Clamp01(value)*100f);
        }
    }

    public void EquipSaber()
    {
        SetEquipped("PRP_Hook_0p22m", false);
        SetEquipped("PRP_Saber_0p92m", true);
        SetHandPose("SKM_Pelag_Body", "Open", 0f);
        SetHandPose("SKM_Pelag_Body", "Fist", 0f);
        SetHandPose("SKM_Pelag_Body", "Grip", 1f);
    }

    public void SheathSaber()
    {
        SetEquipped("PRP_Saber_0p92m", false);
        SetHandPose("SKM_Pelag_Body", "Grip", 0f);
        SetHandPose("SKM_Pelag_Body", "Open", 1f);
    }

    public void EquipHook()
    {
        SetEquipped("PRP_Saber_0p92m", false);
        SetEquipped("PRP_Hook_0p22m", true);
        SetHandPose("SKM_Pelag_Body", "Open", 0f);
        SetHandPose("SKM_Pelag_Body", "Fist", 0f);
        SetHandPose("SKM_Pelag_Body", "Grip", 1f);
    }

    public void StoreHook()
    {
        SetEquipped("PRP_Hook_0p22m", false);
        SetHandPose("SKM_Pelag_Body", "Grip", 0f);
        SetHandPose("SKM_Pelag_Body", "Open", 1f);
    }

    public void SheathAll()
    {
        SetEquipped("PRP_Saber_0p92m", false);
        SetEquipped("PRP_Hook_0p22m", false);
        SetHandPose("SKM_Pelag_Body", "Grip", 0f);
        SetHandPose("SKM_Pelag_Body", "Open", 1f);
    }

    private Transform Find(string objectName)
    {
        return GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == objectName);
    }
}
