/// <summary>A script with a parameterless constructor that notes each construction. Made with CreateInstance only.</summary>
public class CountedData : UnityEngine.ScriptableObject
{
    public int number = 1;

    public CountedData()
    {
        ConstructionLog.Note(this);
    }
}
