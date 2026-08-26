using DotNetReverseProxy.Smtp;

namespace DotNetReverseProxy.Tests.Smtp;

public class SmtpUnitTests
{
    [Fact]
    public async Task ReadLineAsync()
    {
        MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("EHLO a\r\nMAIL FROM:<a@a.com>\nc\r\n"));

        var asr = new AsyncSocketReader(ms);

        var line = await asr.ReadLineAsync();

        Assert.Equal("EHLO a", line);

        line = await asr.ReadLineAsync();

        Assert.Equal("MAIL FROM:<a@a.com>\nc", line);
    }


    [Fact]
    public async Task ReadLineMultipleAsync()
    {
        MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("EHLO a\r\nMAIL FROM:<a@a.com>\nc\r\n"));

        var asr = new AsyncSocketReader(ms);

        var line = await asr.ReadLineAsync();

        Assert.Equal("EHLO a", line);

        line = await asr.ReadLineAsync();

        Assert.Equal("MAIL FROM:<a@a.com>\nc", line);
    }


    //[Fact]
    //public async Task ReadTillLineFeedAsync()
    //{
    //    MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("EHLO a\r\nMAIL FROM:<a@a.com>\nc\r\n"));

    //    var asr = new AsyncSocketReader(ms);

    //    var line = await asr.ReadTillLineFeedAsync();

    //    Assert.Equal("EHLO a\r", line);

    //    line = await asr.ReadTillLineFeedAsync();

    //    Assert.Equal("MAIL FROM:<a@a.com>", line);
    //}
}