namespace DotNetReverseProxy.Forward;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class EndPointHttpClient : HttpClient
{
    public EndPointHttpClient(EndPoint endpoint) : base(CreateEndPointHandler(endpoint))
    {
    }

    private static SocketsHttpHandler CreateEndPointHandler(EndPoint endpoint)
    {
        if (endpoint is UnixDomainSocketEndPoint unix)
        {
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
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
        }
        return new SocketsHttpHandler {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false
        };
    }

}