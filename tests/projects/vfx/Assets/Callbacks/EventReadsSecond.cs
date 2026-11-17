using System.Reflection;

/// <summary>The block after the ones that set attributes.</summary>
public class EventReadsSecond : EventReads
{
    public class InputProperties : Inputs { }

    static readonly FieldInfo[] s_Fields = typeof(InputProperties).GetFields();

    protected override string Block => "second-";

    protected override FieldInfo[] Fields => s_Fields;
}
