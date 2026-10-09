using DotNetReverseProxy.Forward;
using NeuroSpeech;
using NeuroSpeech.Acme;
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

namespace DotNetReverseProxy.HostLookup;

public class BaseHostFinder
{
    private readonly string Host;
    private readonly string protocol;
    private readonly string? queryHostNameRoute;
    private readonly string? forwardJsonFilePath;
    private Dictionary<string, Func<CancellationToken, ValueTask<Stream>>>? ports ;
    private readonly Func<CancellationToken, ValueTask<Stream>>? defaultEndPoint;
    private readonly JsonLogger logger;
    private HttpClient? forwardClient;


    public BaseHostFinder(JsonLogger logger,
        string protocol,
        string host,
        string? key,
        string? forwardJsonFilePath)
    {
        this.Host = host;
        this.protocol = protocol;
        this.queryHostNameRoute = System.Environment.GetEnvironmentVariable("FORWARD_ROUTE");
        if(this.queryHostNameRoute != null)
        {
            var endPoint = ParseEndPoint(this.queryHostNameRoute);
            this.forwardClient = EndPointHttpClient.CreateEndPointHandler(endPoint);
            logger.Log(new {
                action= "Forward Route",
                this.queryHostNameRoute,
                endPoint = endPoint.GetType().Name + ":" + endPoint.ToString()
            });
        }
        this.forwardJsonFilePath = forwardJsonFilePath;
        if (key != null)
        {
            logger.Log(new { 
                this.defaultEndPoint
            });
            this.defaultEndPoint = Factory(ParseEndPoint(key));
        }
        this.logger = logger;
    }

    public async ValueTask<bool> CanServe(string hostName, CancellationToken token = default)
    {
        var index = hostName.IndexOf(':');
        if (index != -1)
        {
            hostName = hostName.Substring(0, index);
        }

        // for the case when cluster might support multiple virtual servers
        // this can query host
        // we should not cache this as cluster server may have recycled and might need
        // restart

        hostName = hostName.ToLower();
        if (this.ports != null)
        {

            if (this.ports.ContainsKey(hostName))
            {
                return true;
            }

            var wildcard = WildcardHelper.Replace(hostName);
            if (wildcard != null)
            {
                if (this.ports.ContainsKey(wildcard))
                {
                    return true;
                }
            }
        }

        // check forward port...
        if (forwardClient != null)
        {
            try
            {
                var r = await this.forwardClient!.GetStringAsync($"/{this.protocol}/{hostName}", token);
                return !string.IsNullOrWhiteSpace(r);
            } catch (Exception ex) {
                logger.LogError(ex);
            }
        }

        return this.defaultEndPoint != null;
    }

    public Func<CancellationToken, ValueTask<Stream>>? GetPort(string hostName)
    {

        var index = hostName.IndexOf(':');
        if(index != -1)
        {
            hostName = hostName.Substring(0, index);
        }

        // for the case when cluster might support multiple virtual servers
        // this can query host
        // we should not cache this as cluster server may have recycled and might need
        // restart

        hostName = hostName.ToLower();
        if (this.ports != null)
        {

            if (this.ports.TryGetValue(hostName, out var port))
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
        }

        // check forward port...
        if (forwardClient != null)
        {
            return (c) => ResolvePortAsync(hostName, c);
        }

        return this.defaultEndPoint;
    }

    private Func<CancellationToken, ValueTask<Stream>> Factory(EndPoint endPoint)
    {
        if (endPoint is UnixDomainSocketEndPoint unixPath)
        {
            return (c) => UnixSocketFactory(unixPath, c);
        }
        if (endPoint is IPEndPoint ipEndPoint)
        {
            return (c) => IPSocketFactory(ipEndPoint, c);
        }
        return (c) => SocketFactory((endPoint as DnsEndPoint)!, c);
    }

    private async ValueTask<Stream> UnixSocketFactory(UnixDomainSocketEndPoint unixPort, CancellationToken cancellationToken)
    {
        IDisposable? disposable = null;
        try
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            disposable = socket;
            await socket.ConnectAsync(unixPort, cancellationToken).ConfigureAwait(false);
            disposable = null;
            return new NetworkStream(socket, true);
        }
        catch (Exception ex)
        {
            logger.Log(new
            {
                action = "failed",
                url = unixPort.ToString(),
                details = ex.ToString()
            });
            throw;
        }
        finally
        {
            disposable?.Dispose();
        }
    }

    private async ValueTask<Stream> IPSocketFactory(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        IDisposable? disposable = null;
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            disposable = socket;
            await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
            disposable = null;
            return new NetworkStream(socket, true);
        }
        catch (Exception ex)
        {
            logger.Log(new
            {
                action = "failed",
                url = endPoint.ToString(),
                details = ex.ToString()
            });
            throw;
        }
        finally
        {
            disposable?.Dispose();
        }
    }

    private async ValueTask<Stream> SocketFactory(DnsEndPoint endPoint, CancellationToken cancellationToken)
    {
        IDisposable? disposable = null;
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            disposable = socket;
            await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
            disposable = null;
            return new NetworkStream(socket, true);
        }
        catch (Exception ex)
        {
            logger.Log(new
            {
                action = "failed",
                url = endPoint.ToString(),
                details = ex.ToString()
            });
            throw;
        }
        finally
        {
            disposable?.Dispose();
        }
    }

    private async ValueTask<Stream> ResolvePortAsync(string hostName, CancellationToken ct)
    {
        //if(!System.IO.File.Exists(queryHostNameRoute))
        //{
        //    return await defaultEndPoint(ct);
        //}

        var r = await this.forwardClient!.GetStringAsync($"/{this.protocol}/{hostName}");
        //logger.Log(new {
        //    action = "route",
        //    hostName,
        //    socket = r
        //});
        if(string.IsNullOrWhiteSpace(r)) {
            // return empty stream...
            return Stream.Null;
        }
        var endPoint = ParseEndPoint(r);
        var factory = Factory(endPoint);
        return await factory(ct);
    }

    private EndPoint ParseEndPoint(string endPoint)
    {

        if (endPoint.StartsWith('/'))
        {
            return new UnixDomainSocketEndPoint(endPoint);
        }

        int port = 0;
        string host = this.Host;
        var tokens = endPoint.Split(':');
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
        if (IPAddress.TryParse(host, out var ip))
        {
            return new IPEndPoint(ip, port);
        }
        return new DnsEndPoint(host, port);
    }

    internal async Task InitAsync()
    {
        var forwardJson = this.forwardJsonFilePath;
        if (forwardJson == null)
        {
            // parse json...
            return;
        }

        this.ports = new ();

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


            if (value is JsonValue jv)
            {
                foreach (var item in jv.ToString().Split(' ', ',', ';'))
                {
                    var h = item.Trim();
                    if (h.Length > 0)
                    {
                        ports[h] = Factory(endPoint);
                    }
                }
                continue;
            }

            if (value is JsonArray array)
            {
                foreach (var item in array)
                {
                    var hostName = item.GetValue<string>().ToLower();
                    foreach (var h in hostName.Split(' ', ',', ';'))
                    {
                        var ht = h.Trim();
                        if (ht.Length > 0)
                        {
                            ports[ht] = Factory(endPoint);
                        }
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

        this.forwardClient = EndPointHttpClient.CreateEndPointHandler(forwardEndPoint);
    }

    internal ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var host = context.InitialRequestMessage.Headers.Host ?? "localhost";
        var factory = GetPort(host);
        return factory!(token);
    }
}
