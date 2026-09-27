using Microsoft.Extensions.Caching.Memory;
using NeuroSpeech.Smtp.Spf;
using System;
using System.Net;
using System.Threading.Tasks;

namespace NeuroSpeech.Smtp;

public class SpfVerificationService
{
    private readonly IMemoryCache cache;
    private readonly JsonLogger logger;

    public SpfVerificationService(IMemoryCache cache, JsonLogger logger)
    {
        this.cache = cache;
        this.logger = logger;
    }

    internal async Task<SmtpStatus?> VerifyAsync(
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
            return SmtpStatus.SpfNotDeclared();
        }

        if(!v.Contains(remoteAddress))
        {
            logger.LogError(new {
                spf= "failed",
                remoteAddress,
                v.Domain,
                ranges = v.IPRanges
            });
            return SmtpStatus.SpfFailed();
        }

        return null;
    }
}
