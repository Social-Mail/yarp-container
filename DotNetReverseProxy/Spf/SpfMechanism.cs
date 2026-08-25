using System.Text.RegularExpressions;

namespace DotNetReverseProxy.Spf;

public readonly struct SpfMechanism
{

    public static SpfMechanism Parse(string input)
    {
        input = input.Trim();

        var r = new Regex("(?<mask>[\\+\\-\\~\\?])?(?<type>[a-z0-9]+)(\\:(?<value>[^\\/\\s]+))?(\\/(?<prefix>.+))?", RegexOptions.Compiled);

        var match = r.Match(input);

        char mask = '+';
        string type = "";
        string value = "";
        string? prefix = null;
        if(match.Groups.TryGetValue("mask", out var maskGroup) && maskGroup.Success)
        {
            mask = maskGroup.ValueSpan[0];
        }
        if(match.Groups.TryGetValue("type", out var typeGroup) && typeGroup.Success)
        {
            type = typeGroup.Value;
        }
        if(match.Groups.TryGetValue("value", out var valueGroup) && valueGroup.Success)
        {
            value = valueGroup.Value;
        }
        if(match.Groups.TryGetValue("prefix", out var prefixGroup) && prefixGroup.Success)
        {
            prefix = prefixGroup.Value;
        }

        return new SpfMechanism(mask, type, value, prefix);
    }

    public readonly char Mask;

    public readonly string Type;

    public readonly string Value;

    public readonly string? Suffix;

    public SpfMechanism(char mask, string type, string value, string? suffix)
    {
        Mask = mask;
        Type = type;
        Value = value;
        Suffix = suffix;
    }

}

