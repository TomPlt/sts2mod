using System;
using System.Linq;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

var dll = "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll";
var decompiler = new CSharpDecompiler(dll, new DecompilerSettings());

// Decompile a specific type by full name
string[] typesToDecompile = args;
if (typesToDecompile.Length == 0)
{
    Console.WriteLine("Usage: dotnet run -- TypeName1 TypeName2 ...");
    return;
}

foreach (var typeName in typesToDecompile)
{
    var fullTypeName = new FullTypeName(typeName);
    try
    {
        var code = decompiler.DecompileTypeAsString(fullTypeName);
        Console.WriteLine($"// ===== {typeName} =====");
        Console.WriteLine(code);
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"// ERROR decompiling {typeName}: {ex.Message}");
    }
}
