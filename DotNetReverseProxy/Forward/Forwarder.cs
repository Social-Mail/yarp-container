using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace DotNetReverseProxy;

public class Forwarder: IMiddleware
{
    private readonly CertificateStore store;
    private readonly IHttpForwarder forwarder;
    private readonly ForwarderRequestConfig requestOptions;
    private readonly HttpMessageInvoker client;

    public Forwarder(CertificateStore store, IHttpForwarder forwarder)
    {
        this.store = store;
        this.forwarder = forwarder;
        this.requestOptions = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(100) };
        this.client = new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            ConnectTimeout = TimeSpan.FromSeconds(15),
            ConnectCallback = async (context, cancellationToken) =>
            {
                Socket? socket = null;
                try
                {
                    var host = context.InitialRequestMessage.Headers.Host;
                    var portAddress = await store.GetPort(host ?? "localhost");
                    if (portAddress.UnixPort != null) {
                        socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                        var port = new UnixDomainSocketEndPoint(portAddress.UnixPort);
                        await socket.ConnectAsync(port, cancellationToken).ConfigureAwait(false);
                    } else
                    {
                        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        var port = new DnsEndPoint(portAddress.Host ?? "localhost", portAddress.Port);
                        await socket.ConnectAsync(port, cancellationToken).ConfigureAwait(false);
                    }
                    return new NetworkStream(socket, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex}");
                    socket?.Dispose();
                    throw;
                }
            }
        });
    }

    public async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {

        var start = DateTime.UtcNow;

        var error = await forwarder.SendAsync(httpContext, "http://" + httpContext.Request.Headers.Host, client, requestOptions,
            (context, proxyRequest) =>
            {
                var responseHeaders = context.Response.Headers;
                responseHeaders.TryAdd("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");
                responseHeaders.TryAdd("X-Content-Type-Options", "nosniff");
                responseHeaders.TryAdd("X-Frame-Options", "SAMEORIGIN");
                responseHeaders.TryAdd("Referrer-Policy", "same-origin");

                // Customize the query string:
                var queryContext = new QueryTransformContext(context.Request);
                var ip = context.Connection.RemoteIpAddress;
                if (ip != null)
                {
                    proxyRequest.Headers.TryAddWithoutValidation("x-forwarded-for", ip.ToString());
                }

                // Assign the custom uri. Be careful about extra slashes when concatenating here. RequestUtilities.MakeDestinationAddress is a safe default.
                proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress("http://" + proxyRequest.Headers.Host, context.Request.Path, queryContext.QueryString);
                proxyRequest.Version = HttpVersion.Version11;
                return default;
            });


        Exception? exception = null;

        // Check if the proxy operation was successful
        if (error != ForwarderError.None)
        {
            var errorFeature = httpContext.Features.Get<IForwarderErrorFeature>();
            if (errorFeature != null)
            {
                exception = errorFeature.Exception;
                if (exception != null)
                {
                    Console.WriteLine(exception.ToString());
                }
            }
        }

        httpContext.RegisterStatus(DateTime.UtcNow - start, exception);
    }
}