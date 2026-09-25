using MimeKit;
using System;

namespace NeuroSpeech.Smtp;

public class SmtpParser
{
    internal static MailboxAddress ParseAddress(string arg)
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

        address = address.Trim().Trim('<', '>');

        return MimeKit.MailboxAddress.Parse(address);
    }
}
