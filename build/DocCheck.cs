// Reads the XML doc comments of the public members under the folders given and fails on a line that talks about the implementation instead of
// the behaviour: allocations, pools, boxing, "costs no string" and the like. A developer who hovers a member in the IDE learns nothing from
// those; AGENTS.md says where such a fact goes instead. Usage: dotnet run build/DocCheck.cs -- <folder> [<folder> ...]
//
// A file counts when it declares a public type at its top level, and in it a doc block is the run of /// lines right before a declaration,
// attributes in between allowed, which is checked when that declaration says public. The cost of a DynamoDB operation is behaviour, so "costs
// twice the capacity" and "costs no extra read" pass, and "costs no string" does not.
//
// File-based apps default to Native AOT, which pins the host's compiler package in the lock file and breaks locked
// restore on any other OS.
#:property PublishAot=false

using System.Text.RegularExpressions;

var banned = new (Regex Pattern, string Why)[]
{
    (new Regex(@"allocat", RegexOptions.IgnoreCase), "allocation"),
    (new Regex(@"\bpool(s|ed|ing)?\b", RegexOptions.IgnoreCase), "pool"),
    (new Regex(@"\bbox(es|ed|ing)?\b", RegexOptions.IgnoreCase), "box"),
    (new Regex(@"\bheap\b", RegexOptions.IgnoreCase), "heap"),
    (new Regex(@"\bno string\b|\bwithout (a|the) string\b", RegexOptions.IgnoreCase), "no string"),
    (new Regex(@"\bhot path\b", RegexOptions.IgnoreCase), "hot path"),
    (new Regex(@"\bzero-copy\b|\bwithout a copy\b", RegexOptions.IgnoreCase), "copy"),
    (new Regex(@"\bdictionary lookup\b", RegexOptions.IgnoreCase), "lookup"),
    (new Regex(@"\bno delegate\b", RegexOptions.IgnoreCase), "delegate"),
    (new Regex(@"\b(costs?|pays?) (no|nothing)\b|\bat no cost\b|\bfor free\b", RegexOptions.IgnoreCase), "cost"),
};

// What DynamoDB charges is behaviour: "costs no extra capacity", "costs no read of it", "costs twice the capacity".
var allowed = new Regex(@"\b(costs?|pays?) (no|nothing)( extra| more)? (capacity|read|write|request|round ?trip|call)", RegexOptions.IgnoreCase);

var publicType = new Regex(@"^public (sealed |static |abstract |readonly |partial |ref )*(class|struct|interface|enum|record|delegate)\b");
var failures = 0;
var files = 0;
foreach (var folder in args)
{
    foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
    {
        var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Array.Exists(parts, static part => part is "obj" or "bin"))
        {
            continue;
        }

        Check(file);
    }
}

Console.WriteLine($"Checked the doc comments of {files} files with public types");
if (failures > 0)
{
    Console.Error.WriteLine($"{failures} line(s) describe the implementation. Say what the member does for the caller, and put the rest in docs/design.md.");
    return 1;
}

return 0;

void Check(string file)
{
    var lines = File.ReadAllLines(file);
    if (!Array.Exists(lines, line => publicType.IsMatch(line)))
    {
        return;
    }

    files++;
    var start = -1;
    for (var index = 0; index < lines.Length; index++)
    {
        var line = lines[index].TrimStart();
        if (line.StartsWith("///", StringComparison.Ordinal))
        {
            start = start < 0 ? index : start;
            continue;
        }

        if (start < 0 || line.Length == 0 || line[0] == '[')
        {
            continue;
        }

        if (line.Contains("public ", StringComparison.Ordinal))
        {
            for (var doc = start; doc < index; doc++)
            {
                Report(file, doc, lines[doc]);
            }
        }

        start = -1;
    }
}

void Report(string file, int index, string line)
{
    if (allowed.IsMatch(line))
    {
        return;
    }

    foreach (var (pattern, why) in banned)
    {
        if (pattern.IsMatch(line))
        {
            failures++;
            Console.WriteLine($"{file}({index + 1}): {why}: {line.Trim()}");
            return;
        }
    }
}
