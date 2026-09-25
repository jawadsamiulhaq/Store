using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Store.Application.Common;

namespace Store.Infrastructure.Services;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@waqasprovisionstore.com";
    public string FromName { get; set; } = "Waqas Provision Store";
}

/// <summary>
/// Writes emails to the log instead of sending them.
/// </summary>
/// <remarks>
/// The default in development. It means password-reset and order-confirmation flows are fully
/// exercisable — the reset link is right there in the console — without configuring a mail server
/// or risking a real message to a real customer from a developer's machine.
/// </remarks>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[Email not sent — no SMTP configured]\n  To: {To}\n  Subject: {Subject}\n  Body:\n{Body}",
            to, subject, htmlBody);

        return Task.CompletedTask;
    }
}

/// <summary>Sends mail over SMTP. Used whenever <c>Smtp:Host</c> is configured.</summary>
public sealed class SmtpEmailSender(
    Microsoft.Extensions.Options.IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(_options.UserName)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.UserName, _options.Password)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };

        message.To.Add(to);

        try
        {
            await client.SendMailAsync(message, ct);
            logger.LogInformation("Sent email to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            // Rethrown so the caller decides. Order confirmation failing is worth a retry;
            // password reset deliberately swallows it to avoid leaking account existence.
            logger.LogError(ex, "Failed to send email to {To}: {Subject}", to, subject);
            throw;
        }
    }
}

public static class EmailRegistration
{
    /// <summary>
    /// Registers SMTP when a host is configured, and the logging sender otherwise — so a fresh
    /// clone runs with no mail configuration at all, and production cannot silently fall back to
    /// not sending mail once <c>Smtp:Host</c> is set.
    /// </summary>
    public static IServiceCollection AddEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(SmtpOptions.SectionName);
        services.Configure<SmtpOptions>(section);

        if (string.IsNullOrWhiteSpace(section["Host"]))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        return services;
    }
}
