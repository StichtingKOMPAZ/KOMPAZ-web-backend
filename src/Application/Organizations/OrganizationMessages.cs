namespace Kompaz.Application.Organizations;

/// <summary>
/// The wording this feature answers a rejected request with, where two requests have to answer alike.
/// <para>
/// Creating an organization and renaming one break the same uniqueness rule, and a person who meets it twice
/// should not be told two different things — so the sentence lives here rather than once in each handler. It names
/// no organization on purpose: the caller typed the name, and repeating it adds nothing to a message that already
/// says what to do about it.
/// </para>
/// </summary>
public static class OrganizationMessages
{
	/// <summary>
	/// Answers a name that is already taken, folded case and all.
	/// </summary>
	public const string NameTaken = "Deze organisatienaam bestaat al. Geef de organisatie een unieke naam.";

	/// <summary>
	/// Answers a missing name. Phrased for the form rather than for the field, because that is the copy the
	/// dialog shows and a second wording would only be a second thing to keep in step.
	/// </summary>
	public const string NameRequired = "Vul alle verplichte velden in.";
}
