using System;

namespace NeuroSpeech.Smtp;

public readonly struct SmtpStatus
{
    public static SmtpStatus SpfFailed()
    {
        return new SmtpStatus(550, "5.7.1", "Permanent rejection because the sending IP address is not authorized by the domain's SPF record.");
    }

    public static SmtpStatus SpfNotDeclared()
    {
        return new SmtpStatus(550, "5.7.26", "Permanent rejection because the sending IP address is not authorized by the domain's SPF record.");
    }

    public static implicit operator SmtpStatus((int code, string extendedCode, string message) x)
    {
        return new SmtpStatus(x.code, x.extendedCode, x.message);
    }

    public static implicit operator string(SmtpStatus status)
    {
        return status.ToString();
    }

    public static SmtpStatus MailFromOk => new SmtpStatus(250, "2.1.5", "OK");

    public static SmtpStatus RcptOk => new SmtpStatus(250, "2.1.5", "OK");

    public static SmtpStatus DataOk => new SmtpStatus(250, "2.1.5", "OK");

    public static SmtpStatus MailboxUnavailable = new SmtpStatus(550, "5.1.1", "Mailbox unavailable");

    public static SmtpStatus InsufficientStorage = new SmtpStatus(550, "5.2.2", "Mailbox full");

    public static SmtpStatus UnknownFailure(string text) {
            if(text.Length > 200)
                    {
                        text = text.Substring(0, 200);
                    }
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        return new SmtpStatus(421, "4.3.2", "Unknown failure " + text);
    }

    public static SmtpStatus FailedParsingMailFrom => (501, "5.1.7", "Failed to parse MAIL FROM address");

    public static SmtpStatus FailedParsingRcpt => (501, "5.1.3", "Failed to parse RCPT address");

    public static SmtpStatus BadSequenceOfCommand => (503, "5.5.1", "Bad Sequence of commands");

    public readonly int Status;

    public readonly string ExtendedStatus;

    public readonly string Message;

    public SmtpStatus(int errorCode, string extendedErrorCode, string message)
    {
        Status = errorCode;
        ExtendedStatus = extendedErrorCode;
        Message = message;
    }

    public override string ToString()
    {
        return $"{Status} {ExtendedStatus} {Message}";
    }
}
