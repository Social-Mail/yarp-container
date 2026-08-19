namespace DotNetReverseProxy.Smtp;

public static class StringExtensions
{

    public static bool IfStartsWith(this string text, string pattern, out string args)
    {
        if (text.StartsWith(text, System.StringComparison.OrdinalIgnoreCase))
        {
            args = text.Substring(pattern.Length).Trim();
            return true;
        }
        args = default!;
        return false;
    }

}
