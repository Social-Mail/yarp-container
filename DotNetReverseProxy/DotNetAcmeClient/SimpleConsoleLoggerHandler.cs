using System;
using System.Diagnostics;
using System.Net.Http;
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
        Console.WriteLine($"[HTTP Request] {request.Method} {request.RequestUri}");

        var stopwatch = Stopwatch.StartNew();
        
        // 2. Pass the request down the pipeline to execute it
        var response = await base.SendAsync(request, cancellationToken);
        
        stopwatch.Stop();

        // 3. Log the incoming response
        Console.WriteLine($"[HTTP Response] {(int)response.StatusCode} {response.StatusCode} ({stopwatch.ElapsedMilliseconds}ms)");

        return response;
    }
}