namespace DynaSharp.UnitTests.Mapping;

[Flags]
internal enum Access : byte
{
    None = 0,
    Read = 1,
    Write = 2,
    Delete = 128,
}
