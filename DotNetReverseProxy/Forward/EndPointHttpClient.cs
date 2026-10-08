namespace DotNetReverseProxy.Forward;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

public class EndPointHttpClient
{
    //public EndPointHttpClient(EndPoint endpoint) : base(CreateEndPointHandler(endpoint))
    //{
    //}

    public static HttpClient CreateEndPointHandler(EndPoint endpoint)
    {
        if (endpoint is UnixDomainSocketEndPoint unix)
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ConnectCallback = async (context, cancellationToken) => {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(endpoint, cancellationToken);
                        return new NetworkStream(socket, true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
            return new HttpClient(handler) {  BaseAddress = new System.Uri("http://localhost") };
        }

        return new HttpClient(new SocketsHttpHandler {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false
        })
        { BaseAddress = new System.Uri("http://localhost") };
    }

}