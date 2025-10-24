

namespace MiniC;

public sealed class SourceTable
{
    public readonly List<Lexer> lexers = [];

    public void Add(string file)
    {
        lexers.Add(new(file));
    }

    public List<Token> GetTokens()
    {
        List<Token> tokens = [];
        while (true)
        {
            var token = lexers[0].NextToken();
            tokens.Add(token);
            if(token.Kind == TokenKind.EOF)
            {
                return tokens;
            }
        }
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

        var ppParser = new PpParser(sources.GetTokens());
        var ppTranslationUnit = ppParser.Parse();

        var env = new MacroEnv();
        var collector = new MacroCollector(env);
        collector.Visit(ppTranslationUnit);

        var projector = new PpProjector(env);
        var expandedTokens = projector.Project(ppTranslationUnit);
        var tu = Parser.Parse(expandedTokens);
        var csEmitter = new CsEmitter(new EmitterConfig());
        var csOutput = csEmitter.Emit(tu);
        File.WriteAllText("file.cs", csOutput);
        //Console.WriteLine(AstPrinter.Dump(tu));
    }
}