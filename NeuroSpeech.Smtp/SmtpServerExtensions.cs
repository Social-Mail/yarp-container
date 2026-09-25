using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NeuroSpeech.Smtp;

public static class SmtpServerExtensions
{
    public static void AddSmtpServer(this IServiceCollection services)
    {
        services.AddSingleton<SpfVerificationService>();
        services.AddScoped<SmtpServerClient>();
        services.AddSingleton<SmtpServer>();

    }
}
