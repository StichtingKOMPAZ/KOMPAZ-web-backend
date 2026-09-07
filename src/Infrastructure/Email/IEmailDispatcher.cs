namespace Kompaz.Infrastructure.Email;

/// <summary>
/// The transport that hands a composed email to the outside world.
/// </summary>
internal interface IEmailDispatcher
{
	Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
