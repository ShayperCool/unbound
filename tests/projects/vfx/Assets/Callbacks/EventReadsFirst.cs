using System.Reflection;

/// <summary>The block before the ones that set attributes.</summary>
public class EventReadsFirst : EventReads
{
    public class InputProperties : Inputs { }

    static readonly FieldInfo[] s_Fields = typeof(InputProperties).GetFields();

    protected override string Block => "first-";

    protected override FieldInfo[] Fields => s_Fields;
}
