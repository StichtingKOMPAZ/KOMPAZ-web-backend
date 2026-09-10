using System.Resources;
using System.Runtime.CompilerServices;

// Dutch is the neutral resource, not a satellite: the product is Dutch, so the language every recipient gets by
// default is the one compiled into the assembly itself. A satellite that failed to deploy then costs a translation
// rather than the primary language.
[assembly: NeutralResourcesLanguage("nl")]

[assembly: InternalsVisibleTo("Kompaz.Application.UnitTests")]
[assembly: InternalsVisibleTo("Application.UnitTests")]
