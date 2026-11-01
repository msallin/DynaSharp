// netstandard2.0 does not define this type, and the compiler needs it for init accessors and records.
// Internal, so a project that sees this assembly through InternalsVisibleTo does not get a second public copy.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
