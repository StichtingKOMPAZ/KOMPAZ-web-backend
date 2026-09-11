using Kompaz.Application.Organizations;
using System.Reflection;

namespace Kompaz.Presentation.Common.Assets;

/// <summary>
/// The image served for an organization that has not uploaded one.
/// <para>
/// It lives here rather than in the database, because it is the same bytes for every organization and nothing may
/// change it: a placeholder row would be one more thing a deployment has to have and a caller could overwrite.
/// Serving it from the logo endpoint rather than leaving the fallback to each client means every client agrees
/// about what an organization without a logo looks like, and none of them ships a second copy of the image.
/// </para>
/// <para>
/// <c>KompazLogo.svg</c> is a stand-in mark, not the brand asset. Replacing the file is the whole change.
/// </para>
/// </summary>
internal static class PlaceholderLogo
{
	private const string ResourceName = "Kompaz.Presentation.Common.Assets.KompazLogo.svg";

	private static readonly Lazy<byte[]> Bytes = new(Read, LazyThreadSafetyMode.ExecutionAndPublication);

	public static string ContentType => LogoImage.SvgContentType;

	/// <summary>
	/// Gets the placeholder's bytes. Read once and held: it is small, and every organization without a logo asks
	/// for it.
	/// </summary>
	public static byte[] Content => Bytes.Value;

	/// <summary>
	/// Reads the embedded asset, and fails loudly if it is not embedded.
	/// <para>
	/// A missing resource means the build stopped embedding it, which is a mistake to hear about at the first
	/// request rather than to paper over with an empty response that renders as a broken image.
	/// </para>
	/// </summary>
	private static byte[] Read()
	{
		using var stream = typeof(PlaceholderLogo).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new InvalidOperationException(
				$"The placeholder logo \"{ResourceName}\" is not embedded in {Assembly.GetExecutingAssembly().GetName().Name}.");

		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);

		return buffer.ToArray();
	}
}
