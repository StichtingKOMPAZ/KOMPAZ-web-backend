using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace Kompaz.Infrastructure.Email;

/// <summary>
/// Delivers email through the configured SMTP relay.
/// </summary>
internal sealed class SmtpEmailDispatcher : IEmailDispatcher
{
	private readonly EmailSettings _settings;

	public SmtpEmailDispatcher(IOptions<EmailSettings> settings)
	{
		_settings = settings.Value;
	}

	public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
	{
		using var mail = new MailMessage
		{
			From = new MailAddress(_settings.FromAddress, _settings.FromName),
			Subject = message.Subject,
			Body = message.Body,
			IsBodyHtml = false,
		};

		mail.To.Add(new MailAddress(message.ToAddress, message.ToName));

		using var client = new SmtpClient(_settings.Smtp.Host, _settings.Smtp.Port)
		{
			EnableSsl = _settings.Smtp.UseStartTls,
		};

		if (!string.IsNullOrWhiteSpace(_settings.Smtp.UserName))
		{
			client.Credentials = new NetworkCredential(_settings.Smtp.UserName, _settings.Smtp.Password);
		}

		await client.SendMailAsync(mail, cancellationToken);
	}
}
