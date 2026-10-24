namespace DotRecast.Recast
{
    internal interface IRcBuilderProgressListener
    {
        void OnProgress(int completed, int total);
    }
}