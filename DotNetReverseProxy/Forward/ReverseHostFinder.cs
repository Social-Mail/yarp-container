using DotNetReverseProxy.Forward;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DotNetReverseProxy;

public class ReverseHostFinder
{
    private readonly string Host;
    private readonly Dictionary<string, EndPoint> ports = new ();
    private readonly EndPoint defaultEndPoint;
    private EndPointHttpClient? forwardClient;

    public ReverseHostFinder()
    {
        this.Host = System.Environment.GetEnvironmentVariable("FORWARD_HOST") ?? "0.0.0.0";
        var key = System.Environment.GetEnvironmentVariable("FORWARD_PORT") ?? "8080";
        this.defaultEndPoint = ParseEndPoint(key);
    }

    public ValueTask<EndPoint> GetPort(string hostName)
    {
        // for the case when cluster might support multiple virtual servers
        // this can query host
        // we should not cache this as cluster server may have recycled and might need
        // restart

        hostName = hostName.ToLower();

        if(this.ports.TryGetValue(hostName, out var port))
        {
            return new ValueTask<EndPoint>(port);
        }

        var wildcard = WildcardHelper.Replace(hostName);
        if (wildcard != null)
        {
            if (this.ports.TryGetValue(wildcard, out port))
            {
                return new ValueTask<EndPoint>(port);
            }
        }

        // check forward port...
        if (forwardClient != null)
        {
            return ResolvePortAsync(hostName);
        }

        return new ValueTask<EndPoint>(this.defaultEndPoint);
    }

    private async ValueTask<EndPoint> ResolvePortAsync(string hostName)
    {
        var r = await this.forwardClient!.GetStringAsync("http://somewhere/fwd/" + hostName);
        return ParseEndPoint(r);
    }

    private EndPoint ParseEndPoint(string endPoint)
    {

        if (endPoint.StartsWith("/"))
        {
            return new UnixDomainSocketEndPoint(endPoint);
        }

        string? host = null;

        int port = 0;
        host = this.Host;
        var tokens = endPoint.Split(":");
        if (tokens.Length > 1)
        {
            host = tokens[0];
            endPoint = tokens[1];
        }
        if (Int32.TryParse(endPoint, out var port1))
        {
            port1 = port;
        }
        else
        {
            port = 80;
        }
        return new DnsEndPoint(host, port1);
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
                    ports[hostName] = endPoint;
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
}
