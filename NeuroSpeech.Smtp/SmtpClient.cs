using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NeuroSpeech.Smtp;

public class SmtpClient: IDisposable
{

    private static void ConsoleJsonLogger(string text)
    {
        Console.WriteLine(text);
    }

    public static async Task<SmtpClient> ConnectAsync(string unixPort, Action<string>? logger, CancellationToken cancellationToken = default)
    {
        logger ??= ConsoleJsonLogger;

        var sc = new SmtpClient(logger);
        await sc.ConnectAsync(unixPort, cancellationToken);

        // do helo...

        return sc;
    }

    private Stream stream;
    private AsyncSocketReader reader;
    private readonly Action<string> logger;

    private async ValueTask<Stream> UnixSocketFactory(
        UnixDomainSocketEndPoint unixPort,
        CancellationToken cancellationToken = default)
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
            this.jsonLog(new
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

    private void jsonLog<T>(T message)
    {
        this.logger(JsonSerializer.Serialize(message));
    }

    public SmtpClient(Action<string> logger)
    {
        this.logger = logger;
    }

    public async Task ConnectAsync(string unixPort, CancellationToken cancellationToken = default)
    {
        var stream = await UnixSocketFactory(new UnixDomainSocketEndPoint(unixPort), cancellationToken);
        this.stream = stream;

        // connect..
        this.reader = new AsyncSocketReader(stream);
    }

    public async Task ConnectAsync(Stream stream)
    {
        this.stream = stream;
        this.reader = new AsyncSocketReader(stream);
    }

    public async Task<SmtpCommandResponse> SendCommand(string command, bool ensureSuccess = true)
    {
        await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(command + "\r\n"));

        return await ReadStatus(ensureSuccess);
    }

    public async Task<SmtpCommandResponse> ReadStatus(bool ensureSuccess = true)
    {
        var r = await SmtpCommandResponse.Parse(() => reader.ReadLineAsync());
        if (ensureSuccess)
        {
            if (r.Status >= 400)
            {
                throw new SmtpException(r.Status, r.Message, r.ExtendedStatus);
            }
        }
        return r;
    }

    public async Task WriteLineAsync(string v)
    {
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(v + "\r\n"));
    }

    public void Dispose()
    {
        try
        {
            stream?.Dispose();
            stream = null;
        } catch
        {

        }
    }
}

public class SmtpException: Exception
{

    public SmtpException(int code, string message, string? extendedCode = default): base(message)
    {
        this.Status = code;
        this.ExtendedStatus = extendedCode;
    }

    public int Status { get; }
    public string? ExtendedStatus { get; }
}
