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

public class ReverseHostFinder: BaseHostFinder
{
    public ReverseHostFinder(JsonLogger logger): base(
        logger,
        System.Environment.GetEnvironmentVariable("FORWARD_HOST") ?? "0.0.0.0",
        System.Environment.GetEnvironmentVariable("FORWARD_PORT"),
        System.Environment.GetEnvironmentVariable("FORWARD_JSON")
    )
    {
    }
}

public class SmtpHostFinder: BaseHostFinder
{
    public SmtpHostFinder(JsonLogger logger)
        : base(
        logger,
        System.Environment.GetEnvironmentVariable("FORWARD_SMTP_HOST") ?? "0.0.0.0",
        System.Environment.GetEnvironmentVariable("FORWARD_SMTP_PORT"),
        System.Environment.GetEnvironmentVariable("FORWARD_SMTP_JSON"))
    {
        
    }
}
