using DotNetReverseProxy.HostLookup;
using MimeKit;
using NeuroSpeech;
using NeuroSpeech.Smtp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DotNetReverseProxy.ForwardSmtp;

public class ForwardSmtpReceiver : ISmtpReceiver
{
    private readonly JsonLogger logger;
    private readonly SmtpHostFinder hostFinder;

    private readonly Dictionary<string, SmtpClient> clients = new Dictionary<string, NeuroSpeech.Smtp.SmtpClient>();

    private MailboxAddress? from;

    public ForwardSmtpReceiver(
        JsonLogger logger,
        SmtpHostFinder hostFinder
    )
    {
        this.logger = logger;
        this.hostFinder = hostFinder;
    }

    public async Task<SmtpStatus> DataAsync(
        SmtpServerClient client,
        MailboxAddress from,
        List<MailboxAddress> to,
        string file)
    {

        await Task.WhenAll(this.clients.Select(async (x) => {

            using var oc = x.Value;
            await oc.SendCommand("DATA");

            using var s = System.IO.File.OpenRead(file);
            await foreach (var line in FileLineReader.ReadLinesAsync(s))
            {
                if(line.StartsWith("."))
                {
                    await oc.WriteLineAsync("." + line);
                    continue;
                }
                await oc.WriteLineAsync(line);
            }
            await oc.WriteLineAsync(".");

            await oc.ReadStatus();
        }));

        this.clients.Clear();

        return SmtpStatus.DataOk;
    }

    public async Task<SmtpStatus> MailFromAsync(SmtpServerClient client, MailboxAddress from)
    {
        this.from = from;
        return SmtpStatus.MailFromOk;
    }

    public async Task<SmtpStatus> RcptToAsync(SmtpServerClient client, MailboxAddress to)
    {
        var domain = to.Domain.ToLower();
        if(!clients.TryGetValue(domain, out var outClient))
        {
            outClient = await CreateNewClient(domain, client);
            clients[domain] = outClient;
        }
        var s = await outClient.SendCommand($"RCPT TO:<{to.ToString()}>", false);
        return new SmtpStatus(s.Status, s.ExtendedStatus, s.Message);
    }

    private async Task<SmtpClient> CreateNewClient(string domain, SmtpServerClient client)
    {
        var factory = this.hostFinder.GetPort(domain);
        var s = await factory(default);

        var outClient = new SmtpClient((text) => Console.WriteLine(text));
        await outClient.ConnectAsync(s);

        var r = await outClient.SendCommand($"EHLO {client.HeloHostName}");

        await outClient.SendCommand($"XCLIENT ADDR={client.RemoteIPAddress}");

        await outClient.SendCommand($"MAIL FROM:<{this.from}>");

        return outClient;
    }
}
