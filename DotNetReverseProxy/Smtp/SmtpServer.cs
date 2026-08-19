using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Smtp;

public class SmtpServer
{
    private readonly JsonLogger logger;
    private readonly IServiceProvider services;
    private TcpListener? server;

    public SmtpServer(JsonLogger logger, IServiceProvider services)
    {
        this.logger = logger;
        this.services = services;
    }

    public void Start()
    {
        try
        {
            this.server = new TcpListener(System.Net.IPAddress.Any, 25);
            this.server.Start();

            Task.Run(this.AcceptSocketAsync);
            logger.Log(new {
                smtp = 25,
                action = "started"
            });
        } catch (Exception ex) {
            logger.LogError(ex);
        }
    }

    private async Task AcceptSocketAsync()
    {
        try
        {
            for (; ; )
            {
                var client = await server!.AcceptTcpClientAsync();
                if (client == null)
                {
                    continue;
                }
                _ = Task.Run(() => this.ProcessClientAsync(client));
            }
        } catch (Exception ex)
        {
            logger.LogError(ex);
        }
    }

    private async Task ProcessClientAsync(TcpClient client)
    {
        try
        {
            using var scope = services.CreateScope();
            using var ssClient = scope.ServiceProvider.GetRequiredService<SmtpServerClient>();

            await ssClient.RunAsync(client);
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
        }
    }
}
