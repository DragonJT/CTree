
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
    private readonly IReadOnlyList<Token> _tokens;
    int index = 0;

    public LexerReader(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
    }

    public Token NextToken()
    {
        return _tokens[index++];
    }
}

public sealed class PpLexerReader : ITokenReader
{
    private readonly SourceTable _sources;
    private readonly int _lexerID = 0;

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
        sources.Add("glad.h");
        //sources.Add("main.c");

        var ppParser = new PpParser(new PpLexerReader(sources));
        var ppTranslationUnit = ppParser.Parse();

        var env = new MacroEnv();
        var collector = new MacroCollector(env);
        collector.Visit(ppTranslationUnit);

        var projector = new PpProjector(env);
        var expandedTokens = projector.Project(ppTranslationUnit);

        var parser = new Parser(new LexerReader(expandedTokens));
        var tu = parser.ParseTranslationUnit();
        Console.WriteLine(AstPrinter.Dump(tu));
        /*var tu = parser.ParseTranslationUnit();
        var interp = new Interpreter();
        var result = interp.Run(tu);
        Console.WriteLine($"exit code: {result}");
        Console.WriteLine(MiniC.AstPrinter.Dump(tu));*/
    }
}