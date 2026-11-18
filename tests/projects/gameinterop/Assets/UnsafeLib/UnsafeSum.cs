namespace ProbeLib
{
    public static class UnsafeSum
    {
        public static unsafe int Of(int a, int b)
        {
            int* values = stackalloc int[2];
            values[0] = a;
            values[1] = b;
            return values[0] + values[1];
        }
    }
}
