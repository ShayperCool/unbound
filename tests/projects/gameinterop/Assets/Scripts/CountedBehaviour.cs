/// <summary>A script with a parameterless constructor that notes each construction. Two scene objects carry it.</summary>
public class CountedBehaviour : UnityEngine.MonoBehaviour
{
    public int number = 1;

    public CountedBehaviour()
    {
        ConstructionLog.Note(this);
    }
}
