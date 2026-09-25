using System.Text.RegularExpressions;

namespace NeuroSpeech.Smtp;

public readonly struct SmtpCommandResponseCode
{

    public static SmtpCommandResponseCode Parse(string line)
    {
        var m = Regex.Match(line, "(?<code>\\d+)((\\-(?<options>([^\\s]+)))|(\\s+(?<message>.+)))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        if (!m.Success)
        {
            throw new InvalidOperationException("Failed to parse " + line);
        }
        if (m.Groups.TryGetValue("code", out var code) && code.Success)
        {
            if (m.Groups.TryGetValue("options", out var options) && options.Success)
            {
                return new SmtpCommandResponseCode(
                    int.Parse(code.ValueSpan),
                    options?.Value,
                    null);
            }
            if(m.Groups.TryGetValue("message", out var message) && message.Success)
            {
                return new SmtpCommandResponseCode(
                int.Parse(code.ValueSpan),
                null,
                message?.Value);
            }
        }
        throw new InvalidOperationException("Failed to parse " + line);
    }
    public readonly int Status;

    public readonly string? Options;

    public readonly string? Message;

    public readonly string? ExtendedStatus;

    public bool IsOption => Options != null;

    public SmtpCommandResponseCode(int status, string? options, string? message)
    {
        this.Status = status;
        this.Options = options;
        if(message != null)
        {
            if(message.Length > 0)
            {
                if (char.IsDigit(message[0]))
                {
                    int index = message.IndexOf(' ');
                    ExtendedStatus = message.Substring(0, index);
                    message = message.Substring(index + 1);
                }
            }
        }
        this.Message = message;
    }
}

public readonly struct SmtpCommandResponse
{
    public readonly int Status;
    public readonly string? ExtendedStatus;
    public readonly string? Message;
    public readonly List<string> Options;

    public SmtpCommandResponse(int status, string? extendedStatus, string? message, List<string> options)
    {
        this.Status = status;
        this.ExtendedStatus = extendedStatus;
        this.Message = message;
        this.Options = options;
    }

    public static async Task<SmtpCommandResponse> Parse(Func<Task<string>> reader)
    {
        List<string> options = new ();
        for (; ; )
        {
            var response = SmtpCommandResponseCode.Parse(await reader());
            if(response.Options != null)
            {
                options.Add(response.Options);
                continue;
            }

            return new SmtpCommandResponse(response.Status, response.ExtendedStatus, response.Message, options);
        }
    }

}
