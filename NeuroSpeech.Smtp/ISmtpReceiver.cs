using MimeKit;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NeuroSpeech.Smtp;

public interface ISmtpReceiver
{

    public Task<SmtpStatus> MailFromAsync(SmtpServerClient client, MailboxAddress from);

    public Task<SmtpStatus> RcptToAsync(SmtpServerClient client, MailboxAddress to);

    public Task<SmtpStatus> DataAsync(SmtpServerClient client, MailboxAddress from, List<MailboxAddress> to, string file);

}
