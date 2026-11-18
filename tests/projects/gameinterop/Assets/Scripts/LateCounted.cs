/// <summary>A script with a parameterless constructor that notes each construction. Never in the scene: added at run time only.</summary>
public class LateCounted : UnityEngine.MonoBehaviour
{
    public int number = 1;

    public LateCounted()
    {
        ConstructionLog.Note(this);
    }
}
