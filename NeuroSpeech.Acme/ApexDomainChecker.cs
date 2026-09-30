using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;
using System;
using System.Collections.Generic;
using System.Text;

namespace NeuroSpeech.Acme;

public class ApexDomainChecker
{

    public static ApexDomainChecker Instance = new ApexDomainChecker();

    private DomainParser? parser;

    public async Task<string?> GetApexDomainAsync(string? domain)
    {
        if (domain == null)
        {
            throw new ArgumentNullException(nameof(domain));
        }
        if(!domain.Contains('.'))
        {
            return null;
        }
        await Build();
        var r = parser!.Parse(domain);
        if (r == null)
        {
            return null;
        }
        return r.RegistrableDomain;

    }

    private async Task Build()
    {
        if (parser == null)
        {
            var ruleProvider = new SimpleHttpRuleProvider();
            await ruleProvider.BuildAsync();
            parser = new DomainParser(ruleProvider);
        }
    }
}
