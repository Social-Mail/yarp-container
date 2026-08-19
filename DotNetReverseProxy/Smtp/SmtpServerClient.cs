using DotNetReverseProxy.Forward;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Smtp;

public class SmtpServerClient : IDisposable
{

    public SmtpServerClient(JsonLogger logger, CertificateStore certificateStore, IMemoryCache cache)
    {
        this.logger = logger;
        this.certificateStore = certificateStore;
        this.cache = cache;
        this.host = System.Environment.GetEnvironmentVariable("SMTP_HOST");
    }

    private string remoteAddress = "";
    private string clientHostName = "";
    private TcpClient? client;
    private Stream? stream;
    private readonly JsonLogger logger;
    private readonly CertificateStore certificateStore;
    private readonly IMemoryCache cache;
    private readonly string? host;
    private bool secure;
    private bool shouldContinue;

    private string? from;
    private List<string>? to;
    private string hostNameAppearsAs;
    private object maxMessageSize;

    public void Dispose()
    {
        
    }

    internal async Task RunAsync(TcpClient client)
    {
        // get remote address...

        await ResolveRemoteIP(client);

        await WriteLineAsync($"220 SocialMail ESMTP 1.0 Ready");
        if(!this.secure)
        {
            await ProcessPlainTextCommands();
            if (this.client == null)
            {
                return;
            }
        }

        this.shouldContinue = true;
        try
        {
            while (this.shouldContinue)
            {
                var line = await this.ReadLineAsync();

                if (line == null)
                {
                    return;
                }

                string arg = "";

                if (line.IfStartsWith("RCPT TO:", out arg))
                {
                    await this.CommandRCPT(arg);
                    continue;
                }

                if (line.IfStartsWith("MAIL FROM:", out arg))
                {
                    await this.CommandMailFrom(arg);
                    continue;
                }

                if(line.IfStartsWith("RSET", out arg))
                {
                    await this.CommandRSET();
                    continue;
                }

                if(line.IfStartsWith("HELO", out arg))
                {
                    await CommandHELO(arg);
                    continue;
                }

                if (line.IfStartsWith("EHLO", out arg))
                {
                    await CommandEHLO(arg);
                    continue;
                }

                if (line.IfStartsWith("DATA", out arg))
                {
                    await CommandData(arg);
                    continue;
                }

                if (line.IfStartsWith("QUIT", out arg))
                {
                    this.shouldContinue = false;
                    break;
                }


            }

            await this.Destroy();
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            await this.Destroy();
        }

    }

    private async Task CommandData(string arg)
    {
        var file = Path.GetTempFileName();
        for(; ;)
        {
            var line = await ReadLineAsync();
            if(line.EndsWith(".\r")) { }
        }
        await this.WriteLineAsync("250 2.0.0 OK");

        this.from = null;
        this.to = null;

    }

    private async Task CommandEHLO(string arg)
    {
        this.hostNameAppearsAs = arg;
        var features = new string[] {
                            "250-OK",
                            $"250-SIZE { this.maxMessageSize}",
                            // "250-8BITMIME", // will support 8BITMIME in future...
                            "250-SMTPUTF8",
                            "250-ENHANCEDSTATUSCODES",
                            "250 OK"
                        };
        await this.WriteLineAsync(string.Join("\n", features));
    }

    private async Task CommandHELO(string arg)
    {
        this.hostNameAppearsAs = arg;
        await this.WriteLineAsync("250 OK");
    }

    private async Task CommandRSET()
    {
        this.from = null;
        this.to = null;
        await this.WriteLineAsync("250 2.1.5 OK");
    }

    private async Task CommandMailFrom(string arg)
    {
        if(this.from != null)
        {
            await this.WriteLineAsync("501 Syntax Error");
            return;
        }
        this.from = SmtpParser.ParseAddress(arg);
        await this.WriteLineAsync("250 2.1.5 OK");
    }

    private async Task CommandRCPT(string arg)
    {
        if(this.from == null)
        {
            await this.WriteLineAsync("501 Syntax Error");
            return;
        }
        (this.to ??= new List<string>()).Add(SmtpParser.ParseAddress(arg));
        await this.WriteLineAsync("250 2.1.5 OK");
    }

    private async Task ProcessPlainTextCommands()
    {
        for(; ;)
        {
            var line = await ReadLineAsync();
            if (string.IsNullOrEmpty(line))
            {
                throw new InvalidOperationException($"Socket sent an empty line");
            }
            var tokens = line.Split(' ');
            switch(tokens[0].ToUpper().Trim())
            {
                case "HELO":
                    await this.SendResponse(250, $"Hello { (tokens.Length > 1 ? tokens[1] : "localhost")}");
                    break;
                case "EHLO":
                    await this.WriteLineAsync("250-OK\n250-REQUIRETLS\n250-STARTTLS\n250 OK");
                    break;
                case "QUIT":
                    await this.Destroy();
                    break;
                case "STARTTLS":
                    await this.UpgradeAsServerTLS();
                    break;
                default:
                    await WriteLineAsync("501 Please use STARTLS before any command");
                    break;
            }
        }
    }

    private Task SendResponse(int code, string text)
    {
        return WriteLineAsync($"{code} {text}");
    }

    async Task Destroy()
    {
        await this.WriteLineAsync("250 Closing Channel");
        this.client?.Dispose();
        this.client = null;
    }

    private async Task UpgradeAsServerTLS()
    {
        var cert = this.host != null
            ? await certificateStore.GetAsync(this.host)
            : await certificateStore.Create24HourCertificate("smtp-server");

        var key = $"ssl-context-{cert.Thumbprint}";

        var secureContext = cache.GetOrCreate(key, (ci) => new SslServerAuthenticationOptions {
            ServerCertificateContext = SslStreamCertificateContext.Create(cert, null),
            AllowRenegotiation = true,
        });

        await this.WriteLineAsync("220 Ready for TLS");

        var s = new SslStream(this.stream!);
        await s.AuthenticateAsServerAsync(secureContext!);

        this.stream = s;
    }

    private async Task WriteLineAsync(string v)
    {
        var buf = System.Text.Encoding.ASCII.GetBytes(v + "\r\n");
        await this.stream!.WriteAsync(buf);
    }

    private async Task ReadLineAsync()
    {

    }

    private async Task ResolveRemoteIP(TcpClient client)
    {
        this.client = client;
        this.stream = client.GetStream();
        if (client.Client.RemoteEndPoint is IPEndPoint ip)
        {
            this.remoteAddress = ip.Address.ToString().Replace("::ffff:", "");
            try
            {
                var r = await Dns.GetHostEntryAsync(ip.Address);
                if (!string.IsNullOrEmpty(r?.HostName))
                {
                    this.clientHostName = r.HostName;
                }
            }
            catch (Exception ex)
            {
                this.clientHostName = this.remoteAddress;
                logger.LogError(ex);
            }
        }
    }
}
