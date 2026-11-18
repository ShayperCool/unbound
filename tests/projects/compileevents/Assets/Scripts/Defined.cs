/// <summary>Whether the scripts were compiled with PROBE_DEFINE (a property: the editor assembly
/// must not embed the value).</summary>
public static class Defined
{
#if PROBE_DEFINE
    public static bool On => true;
#else
    public static bool On => false;
#endif
}
