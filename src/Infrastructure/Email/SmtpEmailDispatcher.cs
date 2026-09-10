using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Text;

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
		// Stated rather than inferred. The wording is Dutch, so subjects, bodies and the names of people are all
		// routinely non-ASCII; MailMessage guesses an encoding from the content when none is given, and a guess
		// that lands on the wrong one is mojibake in somebody's inbox that no test here would see.
		using var mail = new MailMessage
		{
			From = new MailAddress(_settings.FromAddress, _settings.FromName, Encoding.UTF8),
			Subject = message.Subject,
			SubjectEncoding = Encoding.UTF8,
			Body = message.Body,
			BodyEncoding = Encoding.UTF8,
			IsBodyHtml = false,
		};

		mail.To.Add(new MailAddress(message.ToAddress, message.ToName, Encoding.UTF8));

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
