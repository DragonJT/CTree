using System.Diagnostics;

namespace MiniC;

public sealed class SourceTable
{
    public readonly List<Lexer> lexers = [];

    public void Add(string file)
    {
        lexers.Add(new(file));
    }
}

public sealed class LexerReader : ITokenReader
{
    private readonly SourceTable _sources;
    private readonly int _lexerID;

    public LexerReader(SourceTable sources) 
    {
        _sources = sources; 
    }

    public Token NextToken()
    {
        return _sources.lexers[_lexerID].NextToken();
    }
}

public sealed class PpLexerReader : ITokenReader
{
    private readonly SourceTable _sources;
    private readonly int _lexerID;

    public PpLexerReader(SourceTable sources) 
    {
        _sources = sources; 
    }

    public Token NextToken()
    {
        return _sources.lexers[_lexerID].NextToken();
    }
}

static class Program
{
    static void Main()
    {
        // 1) Load files
        var sources = new SourceTable();
        //sources.Add("glad.h");
        sources.Add("main.c");

        var parser = new Parser(new LexerReader(sources));
        var tu = parser.ParseTranslationUnit();
        var interp = new Interpreter();
        var result = interp.Run(tu);
        Console.WriteLine($"exit code: {result}");
        Console.WriteLine(MiniC.AstPrinter.Dump(tu));
    }
}