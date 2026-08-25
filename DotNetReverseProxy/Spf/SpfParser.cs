using Amazon.Runtime.Internal.Util;
using System.Collections.Generic;

namespace DotNetReverseProxy.Spf;

public class SpfParser
{
    public static SpfMechanism[]? Parse(string input)
    {
        input = input.Trim();
        if (!input.StartsWith("v=spf1"))
        {
            return null;
        }

        input = input.Substring(7);

        var tokens = input.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

        var list = new List<SpfMechanism>();


        foreach (var token in tokens)
        {
            list.Add(SpfMechanism.Parse(token));
        }
        return list.ToArray();
    }


}

