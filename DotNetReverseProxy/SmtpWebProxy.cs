using DotNetReverseProxy.HostLookup;
using MimeKit;
using NeuroSpeech;
using NeuroSpeech.Smtp;
using RetroCoreFit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace DotNetReverseProxy;

public readonly struct MailRecipientGroup{

    public readonly string Domain;
    public readonly MailboxAddress From;
    public readonly List<MailboxAddress> To;

    public MailRecipientGroup(string domain, MailboxAddress from)
    {
        this.Domain = domain;
        this.From = from;
        this.To = new List<MailboxAddress>();
    }
}

public class SmtpWebProxy : ISmtpReceiver
{
    private readonly HttpClient httpClient;
    private readonly JsonLogger logger;
    private Dictionary<string, List<MailRecipientGroup>> recipientGroups = new ();

    private string connectUrl(string domain, string path) => $"http://{domain}/social-mail/v2/smtp/in/{path}";

    public SmtpWebProxy(ReverseHostFinder hostFinder, JsonLogger logger)
    {
        this.httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectCallback = hostFinder.ConnectAsync
        });
        this.httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-forwarded-for", "0.0.0.0");
        this.logger = logger;
    }

    public async Task<SmtpStatus> DataAsync(SmtpServerClient client, MailboxAddress from, List<MailboxAddress> to, string file)
    {

        // verify dkim here..

        try
        {
            foreach (var g in recipientGroups)
            {
                using var s = System.IO.File.OpenRead(file);
                var response = await RequestBuilder.Post(connectUrl(g.Key, "data"))
                    .Multipart("helo", client.HeloHostName)
                    .Multipart("reverseDns", client.ReverseDnsName)
                    .Multipart("remoteIPAddress", client.RemoteIPAddress)
                    .Multipart("from", from.ToString())
                    .Multipart("recipients", string.Join(",", g.Value.Select((x) => x.ToString())))
                    .MultipartFile("mail", s)
                    .AsResponseMessageAsync(this.httpClient);

                if (response.IsSuccessStatusCode)
                {
                    continue;
                }

                switch (response.StatusCode)
                {

                    case HttpStatusCode.NotAcceptable:
                        return SmtpStatus.MailboxUnavailable;
                    case HttpStatusCode.InsufficientStorage:
                        return SmtpStatus.InsufficientStorage;
                }
                var text = await response.Content.ReadAsStringAsync();
                return SmtpStatus.UnknownFailure(text);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(new
            {
                error = ex.Message,
                details = ex.ToString()
            });
            return SmtpStatus.UnknownFailure(ex.Message);
        }
        return SmtpStatus.DataOk;
    }

    public Task<SmtpStatus> MailFromAsync(SmtpServerClient client, MailboxAddress from)
    {
        // we cannot verify from without rcpt...
        return Task.FromResult(SmtpStatus.MailFromOk);
    }

    public async Task<SmtpStatus> RcptToAsync(SmtpServerClient client, MailboxAddress to)
    {
        try
        {
            var response = await RequestBuilder.Post(connectUrl(to.Domain, "rcpt"))
                .Body(new {
                    remoteIPAddress = client.RemoteIPAddress,
                    reverseDnsName =  client.ReverseDnsName,
                    heloHostName = client.HeloHostName,
                    from = client.From.ToString(),
                    to = to.ToString(),
                    spf = "success"
                })
                .AsResponseMessageAsync(this.httpClient);

            if (response.IsSuccessStatusCode)
            {
                // add to group..
                if (!recipientGroups.TryGetValue(to.Domain, out var recipientGroupsList))
                {
                    recipientGroupsList = new List<MailRecipientGroup>();
                }
                recipientGroupsList.Add(new MailRecipientGroup(to.Domain, to));
                return SmtpStatus.RcptOk;
            }

            switch (response.StatusCode)
            {

                case HttpStatusCode.NotAcceptable:
                    return SmtpStatus.MailboxUnavailable;
                case HttpStatusCode.InsufficientStorage:
                    return SmtpStatus.InsufficientStorage;
            }
            var text = await response.Content.ReadAsStringAsync();
            return SmtpStatus.UnknownFailure(text);
        } catch (Exception ex)
        {
            logger.LogError(new {
                error = ex.Message,
                details = ex.ToString()
            });
            return SmtpStatus.UnknownFailure(ex.Message);
        }
        
    }
}
