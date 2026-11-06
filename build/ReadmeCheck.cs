// Compiles the C# blocks of pages of this repository, the READMEs and docs/*.md, and the <example> blocks of the XML docs of the source
// folders given, against the projects, and fails on an error, which names the page or file and its line. All pages given go into one
// compilation, so a type or an [assembly: ...] attribute is declared on one page only.
// Usage: dotnet run build/ReadmeCheck.cs -- <page.md> [<page.md> ...] [<folder> ...]
//
// An <example> of the XML docs holds <code> blocks, which go in as the blocks of a page do, with the entities &lt;, &gt;, &amp; and
// &quot; unescaped. A <code> block outside an <example> is an illustration and is not compiled.
//
// A block goes in as it stands on the page, and this adds only what the page leaves out. The types of the page are those of the sample, and the
// names it introduces as it goes, such as client, orders and ct, are the fields of the class Page below. That leaves four kinds of block:
//   - With [assembly: ...] it is a file of its own, because no other place can hold it.
//   - With a type it gets a namespace of its own, so that the Order of the page can stand beside the one of the sample.
//   - With a member and no type it goes into an entity made for it.
//   - Anything else is the body of a method. With using lines of its own it gets those and nothing else, so that they are shown to be enough.
//
// File-based apps default to Native AOT, which pins the host's compiler package in the lock file and breaks locked
// restore on any other OS.
#:property PublishAot=false

using System.Diagnostics;
using System.Globalization;
using System.Text;

const string Fence = "```";
const string SharedUsings = """
    using DynaSharp;
    using DynaSharp.Coordination;
    using DynaSharp.Credentials;
    using DynaSharp.Mapping;
    using DynaSharp.Requests;
    using DynaSharp.Sample;
    using DynaSharp.Serialization;
    using DynaSharp.Streams;
    using DynaSharp.Testing;
    using static DynaSharp.Expr;

    """;
const string PageFields = """
        private static DynamoDbClient client = null!;
        private static Table<Order> orders = null!;
        private static Table<Customer> customers = null!;
        private static Table<Product> products = null!;
        private static Table<Cart> carts = null!;
        private static Table<EmailClaim> emails = null!;
        private static Table<Item> shop = null!;
        private static Index<Order> byStatus = null!;
        private static CancellationToken ct;
        private static Order order = null!;
        private static Customer customer = null!;
        private static Product[] catalog = [];
        private static string[] skus = [];
        private static DateTimeOffset since;
        private static OrderLine[] lines = [];
        private static Cart seen = null!;
        private static string after = "";
        private static StubHandler stub = null!;
        private static TimeProvider clock = null!;
        private static string tokenFile = "";
        private static string roleArn = "";
        private static DynamoDbStreamsClient streams = null!;
        private static string streamArn = "";
        private static string shardId = "";
        private static LockTable locks = null!;
        private static Lease lease = null!;

    """;

var readmes = Array.ConvertAll(args, Path.GetFullPath);
var root = RepositoryOf(readmes[0]);
var output = Path.Combine(root, "artifacts", "readme-check");
Directory.CreateDirectory(output);
foreach (var stale in Directory.GetFiles(output, "*.cs"))
{
    File.Delete(stale);
}

var pageUsings = new StringBuilder();
var pageMethods = new StringBuilder();
var blocks = 0;
foreach (var readme in readmes)
{
    if (Directory.Exists(readme))
    {
        foreach (var file in Directory.EnumerateFiles(readme, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (!file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(static part => part is "obj" or "bin"))
            {
                AddExamplesOf(file);
            }
        }
    }
    else
    {
        AddBlocksOf(readme);
    }
}

// Inside this namespace a type of the sample wins over one of a block, which is how Order stays the sample's while Invoice comes from its block.
Write("Page", $"{SharedUsings}\nnamespace DynaSharp.Sample;\n\n{pageUsings}\ninternal static class Page\n{{\n{PageFields}\n{pageMethods}}}\n");

// Inside the repository the settings of its projects would apply, and they are for code that this project does not hold: every warning fatal, a lock file, central versions.
File.WriteAllText(Path.Combine(output, "Directory.Build.props"), "<Project />\n");
File.WriteAllText(Path.Combine(output, "Directory.Packages.props"), "<Project />\n");
var project = Path.Combine(output, "ReadmeCheck.csproj");
File.WriteAllText(project, """
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net11.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <DebugType>none</DebugType>
      </PropertyGroup>
      <ItemGroup>
        <Compile Include="../../samples/DynaSharp.Sample/ShopModel.cs" />
        <Compile Include="../../samples/DynaSharp.Sample/OrderFeed.cs" />
        <!-- For the pages only: recipes of a BackgroundService. No project of src depends on hosting. -->
        <FrameworkReference Include="Microsoft.AspNetCore.App" />
        <ProjectReference Include="../../src/DynaSharp/DynaSharp.csproj" />
        <ProjectReference Include="../../src/DynaSharp.Coordination/DynaSharp.Coordination.csproj" />
        <ProjectReference Include="../../src/DynaSharp.Testing/DynaSharp.Testing.csproj" />
        <ProjectReference Include="../../src/DynaSharp.Generators/DynaSharp.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
      </ItemGroup>
    </Project>

    """);

Console.WriteLine($"Compiling {blocks} blocks of {readmes.Length} page(s)");
using var build = Process.Start("dotnet", ["build", project, "-c", "Release", "--nologo", "-clp:ErrorsOnly"]);
build.WaitForExit();
return build.ExitCode;

void AddBlocksOf(string readme)
{
    var source = Path.GetRelativePath(output, readme).Replace('\\', '/');

    // The page and the line make the name of a block: docs/tables.md line 12 is docs_tables_md_12.
    var prefix = string.Concat(Path.GetRelativePath(root, readme).Select(static c => char.IsAsciiLetterOrDigit(c) ? c : '_'));
    var page = File.ReadAllLines(readme);
    for (var index = 0; index < page.Length; index++)
    {
        if (page[index] != Fence + "csharp")
        {
            continue;
        }

        var end = Array.IndexOf(page, Fence, index);
        var block = page[(index + 1)..end];
        var line = index + 2;
        var id = string.Create(CultureInfo.InvariantCulture, $"{prefix}_{line}");
        blocks++;
        index = end;
        AddBlock(block, line, id, source);
    }
}

void AddExamplesOf(string file)
{
    var source = Path.GetRelativePath(output, file).Replace('\\', '/');
    var prefix = string.Concat(Path.GetRelativePath(root, file).Select(static c => char.IsAsciiLetterOrDigit(c) ? c : '_'));
    var lines = File.ReadAllLines(file);
    var inExample = false;
    for (var index = 0; index < lines.Length; index++)
    {
        var text = DocText(lines[index]);
        if (text is null)
        {
            inExample = false;
            continue;
        }

        inExample = text switch
        {
            "<example>" => true,
            "</example>" => false,
            _ => inExample,
        };
        if (!inExample || text != "<code>")
        {
            continue;
        }

        var end = index + 1;
        while (DocText(lines[end]) != "</code>")
        {
            end++;
        }

        var block = lines[(index + 1)..end].Select(static line => Unescape(DocText(line) ?? string.Empty)).ToArray();
        var line = index + 2;
        var id = string.Create(CultureInfo.InvariantCulture, $"{prefix}_{line}");
        blocks++;
        index = end;
        AddBlock(block, line, id, source);
    }
}

void AddBlock(string[] block, int line, string id, string source)
{
    // The using lines of a block come first, and the rest keeps its line numbers in the README.
    var first = Array.FindLastIndex(block, IsUsing) + 1;
    var usings = string.Join('\n', block[..first]);
    var body = string.Create(CultureInfo.InvariantCulture, $"#line {line + first} \"{source}\"\n{string.Join('\n', block[first..])}\n#line default\n");
    if (Array.Exists(block, static line => line.StartsWith("[assembly:", StringComparison.Ordinal)))
    {
        Write(id, $"using DynaSharp.Sample;\n{usings}\n{body}");
    }
    else if (Array.Exists(block, IsType))
    {
        Write(id, $"{(first > 0 ? usings + "\nusing DynaSharp.Sample;\n" : SharedUsings)}\nnamespace Readme.Block{id};\n\n{body}");
        pageUsings.Append(CultureInfo.InvariantCulture, $"using Readme.Block{id};\n");
    }
    else if (Array.Exists(block, static line => line.StartsWith("public ", StringComparison.Ordinal)))
    {
        Write(id, $$"""
            {{SharedUsings}}
            namespace Readme.Block{{id}};

            [DynamoDbEntity]
            [Discriminator(OptOut = true)]
            public partial record Host
            {
                [PartitionKey]
                public required string Id { get; init; }

            {{body}}
            }

            """);
    }
    else if (first > 0)
    {
        Write(id, $"{usings}\n\ninternal static class Block{id}\n{{\n    public static async Task Run()\n    {{\n{body}    }}\n}}\n");
    }
    else
    {
        pageMethods.Append(CultureInfo.InvariantCulture, $"    public static async Task Block{id}()\n    {{\n{body}    }}\n\n");
    }
}

void Write(string name, string text) => File.WriteAllText(Path.Combine(output, $"Block{name}.cs"), text);

static string RepositoryOf(string file)
{
    var folder = Path.GetDirectoryName(file)!;
    while (!File.Exists(Path.Combine(folder, "DynaSharp.slnx")))
    {
        folder = Path.GetDirectoryName(folder) ?? throw new ArgumentException($"{file} is not in the repository.");
    }

    return folder;
}

// The text of a /// line without the marker and the one space after it, or null for a line that is no doc comment.
static string? DocText(string line)
{
    var trimmed = line.TrimStart();
    if (!trimmed.StartsWith("///", StringComparison.Ordinal))
    {
        return null;
    }

    var text = trimmed[3..];
    return text.Length > 0 && text[0] == ' ' ? text[1..] : text;
}

static string Unescape(string text) =>
    text.Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal).Replace("&quot;", "\"", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal);

static bool IsUsing(string line) => line.StartsWith("using ", StringComparison.Ordinal) && !line.StartsWith("using var ", StringComparison.Ordinal);

static bool IsType(string line) => line.StartsWith("public ", StringComparison.Ordinal) && (line.Contains(" record ", StringComparison.Ordinal) || line.Contains(" class ", StringComparison.Ordinal) || line.Contains(" struct ", StringComparison.Ordinal));
