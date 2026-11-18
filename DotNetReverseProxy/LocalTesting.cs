using System;

namespace DotNetReverseProxy;

public class LocalTesting
{

    public static bool IsTesting = Environment.GetEnvironmentVariable("LOCAL_TEST")?.Equals("true", StringComparison.OrdinalIgnoreCase)
            ?? false;

}
