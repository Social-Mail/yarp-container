using System;

namespace DotNetReverseProxy.Smtp;

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
