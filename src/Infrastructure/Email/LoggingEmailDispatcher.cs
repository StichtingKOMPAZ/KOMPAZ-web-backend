using Microsoft.Extensions.Logging;

namespace Kompaz.Infrastructure.Email;

/// <summary>
/// Writes outbound email to the log instead of delivering it. Selected while the SMTP relay is not fully configured,
/// so the API is usable straight from a fresh checkout. Because sign-in links end up in the log, this must never be
/// the active sink outside development: point <c>Email:Smtp</c> at Mailtrap or a real relay instead.
/// </summary>
internal sealed class LoggingEmailDispatcher : IEmailDispatcher
{
	private readonly ILogger<LoggingEmailDispatcher> _logger;

	public LoggingEmailDispatcher(ILogger<LoggingEmailDispatcher> logger)
	{
		_logger = logger;
	}

	public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
	{
		_logger.LogWarning(
			"SMTP is not configured, writing email to the log. To: {ToAddress}. Subject: {Subject}.{NewLine}{Body}",
			message.ToAddress,
			message.Subject,
			Environment.NewLine,
			message.Body);

		return Task.CompletedTask;
	}
}
