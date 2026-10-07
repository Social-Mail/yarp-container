using Nager.PublicSuffix;
using System;
using System.Text.RegularExpressions;

namespace NeuroSpeech.Acme;

public class WildcardHelper
{
    public static string? Replace(string hostName)
    {
        var index = hostName.IndexOf('.');
        if (index == -1)
        {
            return hostName;
        }
        return "*." + hostName.Substring(index + 1);
    }

    public static string? ToDirectory(string hostName)
    {
        var index = hostName.IndexOf('.');
        if (index == -1)
        {
            return hostName;
        }
        return hostName.Substring(index + 1);
    }

    public static string? ReplaceAsFileName(string hostName)
    {
        var index = hostName.IndexOf('.');
        if (index == -1)
        {
            return hostName;
        }
        return "$wildcard." + hostName.Substring(index + 1);
    }

}
