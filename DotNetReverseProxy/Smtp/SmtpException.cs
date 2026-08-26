using System;

namespace DotNetReverseProxy.Smtp;

public class SmtpException: Exception
{
    public static SmtpException NewSpfRequired()
    {
        return new SmtpException(550, "5.7.1", "Permanent rejection because the sending IP address is not authorized by the domain's SPF record.");
    }

    public static SmtpException NewSpfNotDeclared()
    {
        return new SmtpException(550, "5.7.26", "Permanent rejection because the sending IP address is not authorized by the domain's SPF record.");
    }

    public int ErrorCode { get; set; }

    public string ExtendedErrorCode { get; set; }

    public SmtpException(int errorCode, string extendedErrorCode, string message) :
        base($"{errorCode}: {message}")
    {
        ErrorCode = errorCode;
        ExtendedErrorCode = extendedErrorCode;
    }
}
