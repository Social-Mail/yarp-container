using NeuroSpeech.Smtp;

namespace DotNetReverseProxy.Tests.Smtp;

public class SmtpAddressParserTests
{

    [Fact]
    public void Parse()
    {
        var r = SmtpParser.ParseAddress("<a@a.com> A1 A2=B2");
        Assert.Equal("a@a.com", r.address.ToString());
        Assert.Equal("A1", r.options["A1"]);
        Assert.Equal("B2", r.options["A2"]);

    }

}
