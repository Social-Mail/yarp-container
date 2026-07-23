using DotNetReverseProxy.Forward;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetReverseProxy;

public class ReverseHostFinder
{
    private readonly string Host;
    private readonly Dictionary<string, Func<CancellationToken,ValueTask<Stream>>> ports = new ();
    private readonly Func<CancellationToken,ValueTask<Stream>> defaultEndPoint;
    private readonly JsonLogger logger;
    private EndPointHttpClient? forwardClient;

    public ReverseHostFinder(JsonLogger logger)
    {
        this.Host = System.Environment.GetEnvironmentVariable("FORWARD_HOST") ?? "0.0.0.0";
        var key = System.Environment.GetEnvironmentVariable("FORWARD_PORT") ?? "8080";
        this.defaultEndPoint = Factory(ParseEndPoint(key));
        this.logger = logger;
    }

    public Func<CancellationToken,ValueTask<Stream>> GetPort(string hostName)
    {
        // for the case when cluster might support multiple virtual servers
        // this can query host
        // we should not cache this as cluster server may have recycled and might need
        // restart

        hostName = hostName.ToLower();

        if(this.ports.TryGetValue(hostName, out var port))
        {
            return port;
        }

        var wildcard = WildcardHelper.Replace(hostName);
        if (wildcard != null)
        {
            if (this.ports.TryGetValue(wildcard, out port))
            {
                return port;
            }
        }

        // check forward port...
        if (forwardClient != null)
        {
            return (c) => ResolvePortAsync(hostName, c);
        }

        return this.defaultEndPoint;
    }

    private Func<CancellationToken,ValueTask<Stream>> Factory(EndPoint endPoint)
    {
        if (endPoint is UnixDomainSocketEndPoint unixPath)
        {
            return (c) => UnixSocketFactory(unixPath, c);
        }
        return (c) => SocketFactory((endPoint as DnsEndPoint)!, c);
    }

    private async ValueTask<Stream> UnixSocketFactory(UnixDomainSocketEndPoint unixPort, CancellationToken cancellationToken)
    {
        IDisposable? disposable = null;
        try {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            disposable = socket;
            await socket.ConnectAsync(unixPort, cancellationToken).ConfigureAwait(false);
            disposable = null;
            return new NetworkStream(socket, true);
        } finally
        {
            disposable?.Dispose();
        }
    }

    private async ValueTask<Stream> SocketFactory(DnsEndPoint endPoint, CancellationToken cancellationToken)
    {
        IDisposable? disposable = null;
        try {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            disposable = socket;
            await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
            disposable = null;
            return new NetworkStream(socket, true);
        } finally
        {
            disposable?.Dispose();
        }
    }

    private async ValueTask<Stream> ResolvePortAsync(string hostName, CancellationToken ct)
    {
        var r = await this.forwardClient!.GetStringAsync("http://somewhere/fwd/" + hostName);
        var endPoint = ParseEndPoint(r);
        var factory = Factory(endPoint);
        return await factory(ct);
    }

    private EndPoint ParseEndPoint(string endPoint)
    {

        if (endPoint.StartsWith("/"))
        {
            return new UnixDomainSocketEndPoint(endPoint);
        }

        int port = 0;
        string host = this.Host;
        var tokens = endPoint.Split(":");
        if (tokens.Length > 1)
        {
            host = tokens[0];
            endPoint = tokens[1];
        }
        if (Int32.TryParse(endPoint, out var port1))
        {
            port = port1;
        }
        else
        {
            port = 80;
        }
        return new DnsEndPoint(host, port);
    }

    internal async Task InitAsync()
    {
        var forwardJson = System.Environment.GetEnvironmentVariable("FORWARD_JSON");
        if (forwardJson == null)
        {
            // parse json...
            return;
        }

        using var fs = File.OpenRead(forwardJson);

        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };
        var root = (await JsonObject.ParseAsync(fs, documentOptions: options)) as JsonObject;

        EndPoint? forwardEndPoint = null;

        foreach (var node in root)
        {
            var value = node.Value;

            var key = node.Key;

            var endPoint = ParseEndPoint(key);


            if(value is JsonArray array)
            {
                foreach(var item in array)
                {
                    var hostName = item.GetValue<string>().ToLower();
                    ports[hostName] = Factory(endPoint);
                }
                continue;
            }

            if (value.GetValueKind() == JsonValueKind.String)
            {
                foreach(var item in value.AsValue().ToString().Split(' ',',', ';'))
                {
                    var h = item.Trim();
                    if(h.Length > 0)
                    {
                        ports[h] = Factory(endPoint);
                    }
                }
                continue;
            }

            forwardEndPoint = endPoint;
            
        }

        if (forwardEndPoint == null)
        {
            return;
        }

        this.forwardClient = new EndPointHttpClient(forwardEndPoint);
    }

    internal ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var host = context.InitialRequestMessage.Headers.Host ?? "localhost";
        var factory = GetPort(host);
        return factory(token);
    }
}
