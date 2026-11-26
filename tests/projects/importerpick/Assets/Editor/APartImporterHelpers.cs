/// <summary>Another file of the partial PartImporter: it declares no other type, and sorts before
/// PartImporter.cs.</summary>
public partial class PartImporter
{
    string Label() => string.IsNullOrEmpty(label) ? "<none>" : label;
}
