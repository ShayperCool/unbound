using System;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

[Serializable]
public struct TbgPlainStruct
{
    public int x;
}

public class ToggleButtonGroupTarget : ScriptableObject
{
    public ToggleButtonGroupState plainState = new ToggleButtonGroupState(0b101UL, 4);
    public ToggleButtonGroupState otherState = new ToggleButtonGroupState(0b10UL, 3);
    public ToggleButtonGroupState emptyState;
    public ToggleButtonGroupState directState = new ToggleButtonGroupState(0b101UL, 3);

    [ToggleButtonGroupStateProperties(allowMultipleSelection: false, allowEmptySelection: false, length: 3)]
    public ToggleButtonGroupState singleChoice;

    [ToggleButtonGroupStateProperties(allowMultipleSelection: true, allowEmptySelection: true, length: -1)]
    public ToggleButtonGroupState keepLengthAttr = new ToggleButtonGroupState(0b1001UL, 4);

    public ToggleButtonGroupState multiSame = new ToggleButtonGroupState(0b101UL, 4);
    public ToggleButtonGroupState multiDiff = new ToggleButtonGroupState(0b101UL, 4);

    public TbgPlainStruct plainStruct = new TbgPlainStruct { x = 7 };
    public int scalarInt = 42;
}

public class ToggleButtonGroupRuntimeModel
{
    [CreateProperty]
    public string tip { get; set; } = "rt-tbg-0";
}
