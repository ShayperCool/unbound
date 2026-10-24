using Unity.Networking.Transport;

/// <summary>Baselib's network addresses as com.unity.transport encodes and decodes them.</summary>
public static class BaselibProbe
{
    public static void RunAll()
    {
        ProbeLog.Begin("baselib.endpoints");
        ProbeLog.Value("loopback4", NetworkEndpoint.LoopbackIpv4.WithPort(7777).Address);
        ProbeLog.Value("loopback6", NetworkEndpoint.LoopbackIpv6.WithPort(7777).Address);
        ProbeLog.Value("any4", NetworkEndpoint.AnyIpv4.Address);
        ProbeLog.Value("parse4", NetworkEndpoint.Parse("192.168.1.20", 80).Address);
        ProbeLog.Value("parse6", NetworkEndpoint.Parse("fe80::1:2", 443).Address);
        ProbeLog.Value("parseInvalid", NetworkEndpoint.TryParse("not an address", 1, out var invalid));
        ProbeLog.Value("parseInvalid.isValid", invalid.IsValid);
        ProbeLog.Value("parse4.family", NetworkEndpoint.Parse("10.0.0.1", 1).Family.ToString());
        ProbeLog.Value("parse6.family", NetworkEndpoint.Parse("::1", 1).Family.ToString());
        ProbeLog.Value("isLoopback", NetworkEndpoint.Parse("127.0.0.1", 9).IsLoopback);
        ProbeLog.Value("isAny", NetworkEndpoint.AnyIpv6.IsAny);
        ProbeLog.Value("port", NetworkEndpoint.Parse("1.2.3.4", 65000).Port);
        ProbeLog.Value("equal", NetworkEndpoint.Parse("1.2.3.4", 5) == NetworkEndpoint.Parse("1.2.3.4", 5));
        ProbeLog.Value("rawLength4", NetworkEndpoint.Parse("1.2.3.4", 5).GetRawAddressBytes().Length);
    }
}
