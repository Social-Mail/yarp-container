using System;
using System.Text.RegularExpressions;

namespace DotNetReverseProxy.Forward;

public class WildcardHelper
{
    private static readonly Regex wildCardReplacer;

    static WildcardHelper()
    {
        string pattern = @"^([^.]*)\.(.*)$";
        wildCardReplacer = new Regex(pattern, RegexOptions.Compiled);
    }

    public static string? Replace(string hostName)
    {
        if(!hostName.Contains("."))
        {
            return null;
        }
        string replacement = "*.$2";
        return wildCardReplacer.Replace(hostName, replacement);
    }

    public static string? ReplaceAsFileName(string hostName)
    {
        if (!hostName.Contains("."))
        {
            return null;
        }
        string replacement = "$wildcard.$2";
        return wildCardReplacer.Replace(hostName, replacement);
    }

    internal static string? GetTopLevel(string hostName)
    {
        if(!hostName.Contains("."))
        {
            return null;
        }
        string replacement = "$2";
        return wildCardReplacer.Replace(hostName, replacement);
    }
}
