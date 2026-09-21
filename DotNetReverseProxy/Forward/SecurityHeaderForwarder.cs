using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace DotNetReverseProxy.Forward;


public class SecurityHeaderForwarder : HttpTransformer
{
    public override ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
    {
        // Customize the query string:
        var queryContext = new QueryTransformContext(httpContext.Request);
        var ip = httpContext.Connection.RemoteIpAddress;
        if (ip != null)
        {
            proxyRequest.Headers.TryAddWithoutValidation("x-forwarded-for", ip.ToString());
        }

        // Assign the custom uri. Be careful about extra slashes when concatenating here. RequestUtilities.MakeDestinationAddress is a safe default.
        //proxyRequest.RequestUri =
        //    RequestUtilities.MakeDestinationAddress(
        //        "http://" + proxyRequest.Headers.Host,
        //        httpContext.Request.Path, queryContext.QueryString);
        //proxyRequest.Version = HttpVersion.Version11;
        // return default;
        return base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
    }

    public override async ValueTask<bool> TransformResponseAsync(
        HttpContext httpContext,
        HttpResponseMessage? proxyResponse,
        CancellationToken cancellationToken)
    {
        var r = await base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
        var responseHeaders = httpContext.Response.Headers;
        TryAdd(responseHeaders, "Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");
        TryAdd(responseHeaders, "X-Content-Type-Options", "nosniff");
        if (responseHeaders.TryGetValue("X-Frame-Options", out var v))
        {
            if (v.Contains("*"))
            {
                responseHeaders.Remove("X-Frame-Options");
            }
        }
        else
        {
            responseHeaders.TryAdd("X-Frame-Options", "SAMEORIGIN");
        }
        TryAdd(responseHeaders, "Referrer-Policy", "same-origin");
        return r;
    }

    private static void TryAdd(IHeaderDictionary headers, string key, string value)
    {
        if (headers.ContainsKey(key))
        {
            return;
        }
        headers.TryAdd(key, value);
    }
}
