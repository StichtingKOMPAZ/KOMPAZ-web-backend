namespace Kompaz.Infrastructure.Email;

/// <summary>
/// A composed email, ready for whichever transport is configured.
/// </summary>
internal sealed record EmailMessage(string ToAddress, string ToName, string Subject, string Body);
