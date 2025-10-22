
static class Program
{
    static void Main()
    {
        var src = File.ReadAllText("main.c");
        var parser = new MiniC.Parser(new MiniC.Lexer(src));
        var tu = parser.ParseTranslationUnit();
        var interp = new MiniC.Interpreter();
        var result = interp.Run(tu);
        Console.WriteLine($"exit code: {result}");
        Console.WriteLine(MiniC.AstPrinter.Dump(tu));
    }
}