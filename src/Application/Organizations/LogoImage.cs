using System.Text;
using System.Text.Unicode;

namespace Kompaz.Application.Organizations;

/// <summary>
/// What an organization logo is allowed to be: a size limit, and a short list of formats recognized by their own
/// bytes.
/// <para>
/// The format is read out of the content rather than taken from the upload's <c>Content-Type</c> or its file
/// extension, both of which the caller chooses freely. That matters because the value is stored and later served
/// back as the response's own media type: believing the upload would let somebody store HTML under
/// <c>image/png</c>, or an executable under any of these, and have this API hand it to a browser as the type it
/// claimed.
/// </para>
/// </summary>
public static class LogoImage
{
	/// <summary>
	/// The largest logo this API accepts. Ten megabytes is generous for a logo and small enough to hold in memory
	/// while the format is checked.
	/// </summary>
	public const int MaximumSizeInBytes = 10 * 1024 * 1024;

	/// <summary>
	/// How the limit is written in the messages that report it, so the number and its unit cannot drift apart.
	/// </summary>
	public const string MaximumSize = "10 MB";

	/// <summary>
	/// The formats accepted, as they read in the message that reports a rejected one.
	/// </summary>
	public const string AcceptedFormats = "PNG, JPEG, SVG of WebP";

	public const string PngContentType = "image/png";

	public const string JpegContentType = "image/jpeg";

	public const string SvgContentType = "image/svg+xml";

	public const string WebPContentType = "image/webp";

	/// <summary>
	/// How far into an SVG the root element is looked for. An SVG may open with a byte-order mark, an XML
	/// declaration, a doctype and comments before it, and this is room for all of them without reading a whole
	/// megabyte of something that is not XML at all.
	/// </summary>
	private const int SvgProbeLength = 1024;

	/// <summary>
	/// U+FEFF, which a UTF-8 byte-order mark decodes to and which is not whitespace, so <c>TrimStart</c> would
	/// leave it in front of the opening angle bracket.
	/// </summary>
	private const char ByteOrderMark = '﻿';

	private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];

	private static readonly byte[] Riff = "RIFF"u8.ToArray();

	private static readonly byte[] WebP = "WEBP"u8.ToArray();

	/// <summary>
	/// Returns the file extension, without a dot, for one of the accepted media types.
	/// <para>
	/// Only used to name the stored file, which nothing reads back — the media type comes from the row, not from
	/// the key. It is worth doing anyway: a bucket somebody is browsing to work out what is taking up space is
	/// far more use when the files say what they are.
	/// </para>
	/// </summary>
	public static string ExtensionFor(string contentType) => contentType switch
	{
		PngContentType => "png",
		JpegContentType => "jpg",
		SvgContentType => "svg",
		WebPContentType => "webp",
		_ => "bin",
	};

	/// <summary>
	/// Reports whether a payload of this many bytes is over the limit. Takes a <see cref="long"/> because the
	/// caller learns the length from the request before it has anything in memory.
	/// </summary>
	public static bool ExceedsMaximumSize(long byteCount) => byteCount > MaximumSizeInBytes;

	/// <summary>
	/// Returns the media type the bytes actually are, or <see langword="null"/> when they are not one of the
	/// accepted formats.
	/// </summary>
	public static string? DetectContentType(ReadOnlySpan<byte> content)
	{
		if (content.StartsWith(Png))
		{
			return PngContentType;
		}

		if (content.StartsWith(Jpeg))
		{
			return JpegContentType;
		}

		// A WebP file is a RIFF container whose four-byte form type says which kind. The length in between is the
		// file's own, so it is skipped rather than read.
		if (content.Length >= 12 && content.StartsWith(Riff) && content[8..12].SequenceEqual(WebP))
		{
			return WebPContentType;
		}

		return IsSvg(content) ? SvgContentType : null;
	}

	/// <summary>
	/// Recognizes an SVG, which unlike the others has no magic number — it is XML, so the evidence is a document
	/// whose root element is <c>svg</c>.
	/// </summary>
	private static bool IsSvg(ReadOnlySpan<byte> content)
	{
		var probe = content[..Math.Min(content.Length, SvgProbeLength)];

		// Only checked when the probe is the whole payload: one cut out of a longer file can end mid-character
		// through no fault of the file, and a lone invalid byte anywhere else would become a replacement character
		// and be compared as though it were text.
		if (probe.Length == content.Length && !Utf8.IsValid(probe))
		{
			return false;
		}

		return OpensWithSvgElement(Encoding.UTF8.GetString(probe).TrimStart(ByteOrderMark));
	}

	/// <summary>
	/// Whether the document's <em>root</em> element is an <c>svg</c>.
	/// <para>
	/// The root, not the first mention. An HTML page containing an inline chart contains <c>&lt;svg&gt;</c> too,
	/// and accepting it would let this API store a page and serve it back as an image — which, for a document
	/// served from this origin, is the whole thing the format check exists to prevent.
	/// </para>
	/// </summary>
	private static bool OpensWithSvgElement(ReadOnlySpan<char> head)
	{
		while (true)
		{
			head = head.TrimStart();

			if (head.IsEmpty || head[0] != '<')
			{
				return false;
			}

			if (head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			int skip = PrologueLength(head);

			if (skip <= 0)
			{
				return false;
			}

			head = head[skip..];
		}
	}

	/// <summary>
	/// The length of the XML declaration, doctype or comment at the start of the span, or <c>-1</c> for anything
	/// else — which is what makes the walk above stop at the first real element rather than search the document.
	/// </summary>
	private static int PrologueLength(ReadOnlySpan<char> head)
	{
		if (head.StartsWith("<!--", StringComparison.Ordinal))
		{
			int end = head.IndexOf("-->");

			return end < 0 ? -1 : end + 3;
		}

		if (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
			|| head.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
		{
			int end = head.IndexOf('>');

			return end < 0 ? -1 : end + 1;
		}

		return -1;
	}
}
