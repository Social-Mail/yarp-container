using System.Collections.Generic;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Smtp;

public interface ISmtpReceiver
{

    public Task MailFromAsync(SmtpServerClient client, string from);

    public Task RcptToAsync(SmtpServerClient client, string to);

    public Task DataAsync(SmtpServerClient client, string from, List<string> to, string file);

}
