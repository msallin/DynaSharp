using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A struct with a constructor of its own, whose body must run on a read.</summary>
[DynamoDbDocument]
internal partial struct Window
{
    public Window()
    {
        Unit = "seconds";
    }

    public string Unit { get; set; }

    public int Length { get; set; }
}
