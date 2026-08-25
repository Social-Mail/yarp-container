using System.Collections.Generic;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Smtp;

public class SmtpWebProxy : ISmtpReceiver
{

    public SmtpWebProxy()
    {
        
    }

    public Task DataAsync(SmtpServerClient client, string from, List<string> to, string file)
    {
        throw new System.NotImplementedException();
    }

    public Task MailFromAsync(SmtpServerClient client, string from)
    {
        throw new System.NotImplementedException();
    }

    public Task RcptToAsync(SmtpServerClient client, string to)
    {
        throw new System.NotImplementedException();
    }
}
