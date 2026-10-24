using System.Text.RegularExpressions;

namespace ProfitGraph.Tests.Unit.Architecture;

/// <summary>
///     Every source and test file declares exactly one type and no nested types; the file is named after the type.
/// </summary>
public sealed partial class OneTypePerFileTests
{
    [Test]
    public void EachFileDeclaresExactlyOneType()
    {
        string[] offenders =
        [
            .. RepositoryLayout.SourceFiles()
                .Concat(RepositoryLayout.TestFiles())
                .Select(path => (Path: path, Count: TypeDeclaration().Count(File.ReadAllText(path))))
                .Where(file => file.Count != 1)
                .Select(file => $"{Path.GetFileName(file.Path)} declares {file.Count} types")
        ];

        offenders.ShouldBeEmpty("Move every type, including nested ones, into its own file.");
    }

    [Test]
    public void DeclarationPatternRecognizesNestedTypes()
    {
        const string code = "public sealed class Outer\n{\n    private sealed record Inner(string Name);\n\n    internal enum Kind { A }\n}\n";

        TypeDeclaration().Count(code).ShouldBe(expected: 3);
    }

    [GeneratedRegex(
        @"^[ \t]*(?:(?:public|internal|private|protected|file|sealed|static|abstract|partial|readonly|unsafe|new)\s+)*(?:record\s+struct|record|class|struct|interface|enum)\s+[A-Z]\w*",
        RegexOptions.Multiline)]
    private static partial Regex TypeDeclaration();
}