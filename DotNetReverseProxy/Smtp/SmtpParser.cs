using System;

namespace DotNetReverseProxy.Smtp;

public class SmtpParser
{
    internal static string? ParseAddress(string arg)
    {
        var smtpUTF8 = false;
        var address = arg;
        if (address.Contains("SMTPUTF8", StringComparison.OrdinalIgnoreCase))
        {
            smtpUTF8 = true;
            address = address.Replace("SMTPUTF8", "", StringComparison.OrdinalIgnoreCase).Trim();
        }
        else
        {
            address = address.Trim();
        }


        return address;
    }
}
