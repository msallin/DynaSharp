// Fails when line or branch coverage in a Cobertura report is below the required percentage.
// Usage: dotnet run build/CoverageGate.cs -- <cobertura.xml> <minimum-percent>
// File-based apps default to Native AOT, which pins the host's compiler package in the lock file and breaks locked
// restore on any other OS.
#:property PublishAot=false

using System.Globalization;
using System.Xml.Linq;

var root = XDocument.Load(args[0]).Root!;
var minimum = double.Parse(args[1], CultureInfo.InvariantCulture);
var failed = false;

foreach (var metric in new[] { "line-rate", "branch-rate" })
{
    var actual = double.Parse(root.Attribute(metric)!.Value, CultureInfo.InvariantCulture) * 100;
    Console.WriteLine($"{metric}: {actual:F2}% (minimum {minimum:F2}%)");
    failed |= actual < minimum;
}

return failed ? 1 : 0;
