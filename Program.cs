

static class Program
{
    static void Main()
    {
        var src = @"
int add(int a, int b) {
    int x = 1 + 2 * 3;
    return a + b + x;
}

int main(int argc){
    for(int i=0;i<10;i++){
        if (i == 3) continue;
        if (i == 7) break;
        print(i);
    }
    return add(3,4);
}
";
        var parser = new MiniC.Parser(new MiniC.Lexer(src));
        var tu = parser.ParseTranslationUnit();
        var interp = new MiniC.Interpreter();
        var result = interp.Run(tu);
        Console.WriteLine(MiniC.AstPrinter.Dump(tu));
        Console.WriteLine($"exit code: {result}");
    }
}