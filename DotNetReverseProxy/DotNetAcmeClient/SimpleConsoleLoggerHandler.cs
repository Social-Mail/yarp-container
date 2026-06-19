using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetAcmeClient;
public class SimpleConsoleLoggerHandler : DelegatingHandler
{
    // Constructor required if you want to chain standard handlers manually
    public SimpleConsoleLoggerHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 1. Log the outgoing request
        var stopwatch = Stopwatch.StartNew();
        
        // 2. Pass the request down the pipeline to execute it
        var response = await base.SendAsync(request, cancellationToken);
        
        stopwatch.Stop();


        var url = request.RequestUri!.ToString();
        if(!url.EndsWith("/new-nonce")) {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                name = "AcmeClient",
                url,
                method = request.Method,
                status = response.StatusCode,
                duration = $"{stopwatch.ElapsedMilliseconds}ms"
            }));
        }

        return response;
    }
}