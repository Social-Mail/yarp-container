using NeuroSpeech.Smtp;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetReverseProxy.Tests.Smtp;

public class SmtpResponseParserTests
{
    [Fact]
    public void Parse()
    {
        var m = SmtpCommandResponseCode.Parse("250 ok");
        Assert.Equal(250, m.Status);
        Assert.Null(m.ExtendedStatus);
        Assert.Equal("ok", m.Message);
        Assert.Null(m.Options);

        m = SmtpCommandResponseCode.Parse("250 2.5.1 Ok");
        Assert.Equal(250, m.Status);
        Assert.Equal("Ok", m.Message);
        Assert.Equal("2.5.1", m.ExtendedStatus);
        Assert.Null(m.Options);

        m = SmtpCommandResponseCode.Parse("250-SMTPUTF8");
        Assert.Equal(250, m.Status);
        Assert.Null(m.Message);
        Assert.Null(m.ExtendedStatus);
        Assert.Equal("SMTPUTF8", m.Options);

        m = SmtpCommandResponseCode.Parse("354 Start mail input; end with <CR><LF>.<CR><LF>");
        Assert.Equal(354, m.Status);
        Assert.Equal("Start mail input; end with <CR><LF>.<CR><LF>", m.Message);
        Assert.Null(m.ExtendedStatus);
        Assert.Null(m.Options);


    }

    [Fact]
    public async Task ParseAsync()
    {
        var lines = new MemoryStream();
        lines.Write(System.Text.Encoding.UTF8.GetBytes("250-SMTPUTF8\n"));
        lines.Write(System.Text.Encoding.UTF8.GetBytes("250-EXTENDED\n"));
        lines.Write(System.Text.Encoding.UTF8.GetBytes("250 2.5.1 Ok\n"));
        lines.Seek (0, SeekOrigin.Begin);

        var reader = new StreamReader(lines);

        var s = await SmtpCommandResponse.Parse(() => reader.ReadLineAsync());

        Assert.Equal(250, s.Status);
        Assert.Equal("2.5.1", s.ExtendedStatus);
        Assert.Equal("Ok", s.Message);

        Assert.Contains("SMTPUTF8", s.Options);
        Assert.Contains("EXTENDED", s.Options);
    }

}
