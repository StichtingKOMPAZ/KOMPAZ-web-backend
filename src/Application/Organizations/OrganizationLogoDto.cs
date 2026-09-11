namespace Kompaz.Application.Organizations;

/// <summary>
/// An organization's logo on its way back out: the bytes, and the media type they were recognized as when they
/// were uploaded.
/// <para>
/// The only DTO here without a <c>Projection</c>, and deliberately: half of it does not live in the database. The
/// media type comes from the row and the bytes come from the file store, so there is no single query to express —
/// the handler reads one, then the other.
/// </para>
/// </summary>
public sealed record OrganizationLogoDto(string ContentType, byte[] Content);
