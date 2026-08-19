using Microsoft.Extensions.DependencyInjection;

namespace DotNetReverseProxy.Smtp;

public static class SmtpServerExtensions
{
    public static void AddSmtpServer(this IServiceCollection services)
    {
        services.AddScoped<SmtpServerClient>();
        services.AddSingleton<SmtpServer>();
    }
}
