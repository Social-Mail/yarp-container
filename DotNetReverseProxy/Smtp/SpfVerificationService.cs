using DotNetReverseProxy.Spf;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Net;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Smtp;

public class SpfVerificationService
{
    private readonly IMemoryCache cache;

    public SpfVerificationService(IMemoryCache cache)
    {
        this.cache = cache;
    }

    internal async Task VerifyAsync(
        string from,
        string remoteAddress,
        string hostNameAppearsAs,
        string clientHostName)
    {

        MimeKit.MailboxAddress address = MimeKit.MailboxAddress.Parse(from);

        var spfKey = $"_spf_{address.Domain.ToLower()}";

        var v = await cache.GetOrCreateAsync(spfKey, (x) => SpfValidator.Fetch(address.Domain));
        if(v == null)
        {
            throw SmtpException.NewSpfNotDeclared();
        }

        if(!v.Contains(remoteAddress))
        {
            throw SmtpException.NewSpfRequired();
        }
    }
}
