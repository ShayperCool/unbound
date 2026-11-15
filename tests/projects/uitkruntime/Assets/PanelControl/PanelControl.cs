using UnityEngine;
using UnityEngine.UIElements;

public class PanelControl : MonoBehaviour, IRuntimePanelComponent
{
    public int assignments;
    IPanel current;
    public IPanel panel { get => current; set { current = value; assignments++; } }
}
