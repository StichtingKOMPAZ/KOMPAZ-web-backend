using FluentAssertions;
using Kompaz.Application.Organizations;
using NUnit.Framework;
using System.Text;

namespace Kompaz.Application.UnitTests.Organizations;

/// <summary>
/// What counts as a logo. The value this decides is stored and later served back as a response's own media type,
/// so a format recognized wrongly is a file this API hands to a browser labelled as something it is not.
/// </summary>
[TestFixture]
internal sealed class LogoImageTests
{
	[Test]
	public void APngIsRecognizedByItsSignature()
	{
		byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

		LogoImage.DetectContentType(content).Should().Be(LogoImage.PngContentType);
	}

	[Test]
	public void AJpegIsRecognizedByItsSignature()
	{
		byte[] content = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

		LogoImage.DetectContentType(content).Should().Be(LogoImage.JpegContentType);
	}

	/// <summary>
	/// WebP announces itself twice: a RIFF container, and a form type four bytes past the length.
	/// </summary>
	[Test]
	public void AWebPIsRecognizedByItsContainerAndFormType()
	{
		byte[] content = [.. "RIFF"u8, 0x20, 0x00, 0x00, 0x00, .. "WEBP"u8, .. "VP8 "u8];

		LogoImage.DetectContentType(content).Should().Be(LogoImage.WebPContentType);
	}

	/// <summary>
	/// A RIFF file that is not WebP is a WAV or an AVI, and neither is a logo.
	/// </summary>
	[Test]
	public void ARiffFileThatIsNotWebPIsRejected()
	{
		byte[] content = [.. "RIFF"u8, 0x20, 0x00, 0x00, 0x00, .. "WAVE"u8, .. "fmt "u8];

		LogoImage.DetectContentType(content).Should().BeNull();
	}

	[TestCase("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
	[TestCase("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
	[TestCase("\n\t<!-- a mark --><svg></svg>")]
	[TestCase("<SVG></SVG>")]
	public void AnSvgIsRecognizedHoweverItOpens(string markup)
	{
		LogoImage.DetectContentType(Encoding.UTF8.GetBytes(markup)).Should().Be(LogoImage.SvgContentType);
	}

	/// <summary>
	/// The byte-order mark is not whitespace, so trimming has to know about it or every SVG saved by an editor
	/// that writes one would be refused.
	/// </summary>
	[Test]
	public void AnSvgIsRecognizedBehindAByteOrderMark()
	{
		byte[] content = [0xEF, 0xBB, 0xBF, .. "<svg></svg>"u8];

		LogoImage.DetectContentType(content).Should().Be(LogoImage.SvgContentType);
	}

	/// <summary>
	/// The reason the root element is checked rather than just looked for: HTML that mentions an SVG somewhere is
	/// still HTML, and serving it as an image would serve a page.
	/// </summary>
	[TestCase("<html><body><svg></svg></body></html>")]
	[TestCase("not markup at all")]
	[TestCase("<!DOCTYPE html><html></html>")]
	public void SomethingThatIsNotAnAcceptedImageIsRejected(string text)
	{
		LogoImage.DetectContentType(Encoding.UTF8.GetBytes(text)).Should().BeNull();
	}

	[Test]
	public void BinaryRubbishIsRejectedRatherThanReadAsText()
	{
		byte[] content = [0x3C, 0xFF, 0xFE, 0xFD, 0x00, 0x73, 0x76, 0x67];

		LogoImage.DetectContentType(content).Should().BeNull();
	}

	[Test]
	public void NothingAtAllIsNotAnImage()
	{
		LogoImage.DetectContentType([]).Should().BeNull();
	}

	/// <summary>
	/// A four-byte file cannot be a WebP, and the container check reads twelve bytes to find out — so the length
	/// has to be established before the bytes are compared.
	/// </summary>
	[Test]
	public void AFileShorterThanASignatureIsRejectedRatherThanReadPastItsEnd()
	{
		byte[] content = [.. "RIFF"u8];

		LogoImage.DetectContentType(content).Should().BeNull();
	}

	[TestCase(0, false)]
	[TestCase(LogoImage.MaximumSizeInBytes - 1, false)]
	[TestCase(LogoImage.MaximumSizeInBytes, false)]
	[TestCase(LogoImage.MaximumSizeInBytes + 1, true)]
	public void TheLimitIsInclusive(long byteCount, bool rejected)
	{
		LogoImage.ExceedsMaximumSize(byteCount).Should().Be(rejected);
	}
}
