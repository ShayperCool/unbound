namespace DotRecast.Core
{
    internal interface IRcRand
    {
        float Next();
        double NextDouble();
        int NextInt32();
    }
}