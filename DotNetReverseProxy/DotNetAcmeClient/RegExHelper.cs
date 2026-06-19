using System.Text.RegularExpressions;

namespace DotNetAcmeClient;

public static class RegExHelper
{

    private static Regex isFinished = new Regex("(valid|ready)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex isReady = new Regex("^(valid|ready)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsReady(string status)
    {
        return isReady.IsMatch(status);
    }

    public static bool IsFinished(string status)
    {
        return isFinished.IsMatch(status);
    }

}
