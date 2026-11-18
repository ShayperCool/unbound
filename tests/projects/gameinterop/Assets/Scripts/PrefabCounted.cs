/// <summary>A script with a parameterless constructor that notes each construction. Only on Resources/Counted.prefab.</summary>
public class PrefabCounted : UnityEngine.MonoBehaviour
{
    public int number = 1;

    public PrefabCounted()
    {
        ConstructionLog.Note(this);
    }
}
