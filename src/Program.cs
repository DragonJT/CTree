using System.ComponentModel;
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

public sealed class LexerReader
{
    private readonly SourceTable _sources;
    private readonly int _lexerID = 0;
    private readonly Dictionary<string, TokenKind> _keywords = [];
    private readonly Dictionary<string, TokenKind> _ppKeywords = [];
    private bool isPP = true;

    public LexerReader(SourceTable sources)
    {
        _keywords.Add("extern", TokenKind.Extern);
        _keywords.Add("return", TokenKind.Return);
        _keywords.Add("if", TokenKind.If);
        _keywords.Add("else", TokenKind.Else);
        _keywords.Add("while", TokenKind.While);
        _keywords.Add("for", TokenKind.For);
        _keywords.Add("break", TokenKind.Break);
        _keywords.Add("continue", TokenKind.Continue);
        _keywords.Add("NULL", TokenKind.Null);
        _keywords.Add("typedef", TokenKind.Typedef);
        _keywords.Add("struct", TokenKind.Struct);

        _ppKeywords.Add("define", TokenKind.Define);
        _ppKeywords.Add("undef", TokenKind.Undef);
        _ppKeywords.Add("include", TokenKind.Include);
        _ppKeywords.Add("if", TokenKind.If);
        _ppKeywords.Add("ifdef", TokenKind.Ifdef);
        _ppKeywords.Add("ifndef", TokenKind.Ifndef);
        _ppKeywords.Add("elif", TokenKind.Elif);
        _ppKeywords.Add("else", TokenKind.Else);
        _ppKeywords.Add("endif", TokenKind.Endif);
        _sources = sources; 
    }

    public Token NextToken()
    {
        return _sources.lexers[_lexerID].NextToken(isPP ? _ppKeywords : _keywords);
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

        var ppParser = new PpParser(new LexerReader(sources));
        var ppTranslationUnit = ppParser.Parse();
        PpAstPrinter.DumpToConsole(ppTranslationUnit, false, false);

        /*var tu = parser.ParseTranslationUnit();
        var interp = new Interpreter();
        var result = interp.Run(tu);
        Console.WriteLine($"exit code: {result}");
        Console.WriteLine(MiniC.AstPrinter.Dump(tu));*/
    }
}