using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
// Форматирование средствами Roslyn из установленного SDK, без внешних пакетов.
foreach (var directory in new[] { "src", "tests" })
foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Where(x => !x.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !x.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
{
    var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
    File.WriteAllText(file, root.NormalizeWhitespace(indentation: "    ", eol: "\n").ToFullString() + "\n");
}
