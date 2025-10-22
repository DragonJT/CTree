using System.Text;

namespace MiniC
{
    public static class AstPrinter
    {
        public static string Dump(Node root, bool showPositions = false)
        {
            var sb = new StringBuilder();
            WriteNode(sb, prefix: "", isLast: true, Label(root, showPositions), Children(root, showPositions));
            return sb.ToString();
        }

        public static void DumpToConsole(Node root, bool showPositions = false)
            => Console.WriteLine(Dump(root, showPositions));

        // ├── / └── style pretty tree
        private static void WriteNode(
            StringBuilder sb,
            string prefix,
            bool isLast,
            string label,
            IReadOnlyList<(string edge, Node child)> children)
        {
            var branch = prefix.Length == 0 ? "" : (isLast ? "└── " : "├── ");
            sb.Append(prefix).Append(branch).Append(label).Append('\n');

            var childPrefix = prefix + (prefix.Length == 0 ? "" : (isLast ? "    " : "│   "));
            for (int i = 0; i < children.Count; i++)
            {
                var (edge, child) = children[i];
                bool last = i == children.Count - 1;
                // edge label line
                sb.Append(childPrefix)
                  .Append(last ? "└── " : "├── ")
                  .Append('[').Append(edge).Append(']').Append('\n');

                // child node line(s)
                var grandChildren = Children(child, showPositions: label.Contains("@"));
                WriteNode(sb,
                          childPrefix + (last ? "    " : "│   "),
                          true,
                          Label(child, label.Contains("@")),
                          grandChildren);
            }
        }

        private static string Label(Node n, bool showPos)
        {
            string pos = showPos ? $" @{n.Pos}" : "";

            return n switch
            {
                TranslationUnit _ => $"TranslationUnit{pos}",
                FuncDef f         => $"FuncDef {f.RetType} {f.Name}{pos}",
                ParamDecl p       => $"Param {p.TypeName} {p.Name}{pos}",
                VarDecl v         => v.Init is null
                                        ? $"VarDecl {v.TypeName} {v.Name}{pos}"
                                        : $"VarDecl {v.TypeName} {v.Name} = …{pos}",

                CompoundStmt _    => $"Block{pos}",
                ReturnStmt r      => r.Expr is null ? $"Return{pos}" : $"Return …{pos}",
                ExprStmt s        => s.Expr is null ? $"ExprStmt ;{pos}" : $"ExprStmt …;{pos}",
                IfStmt _          => $"If{pos}",
                ForStmt _         => $"For{pos}",
                BreakStmt _       => $"Break{pos}",
                ContinueStmt _    => $"Continue{pos}",

                IntegerExpr i     => $"Int {i.Value}{pos}",
                IdentExpr id      => $"Ident {id.Name}{pos}",
                AssignExpr _      => $"Assign{pos}",
                CallExpr _        => $"Call{pos}",
                UnaryExpr u       => u.Op switch
                {
                    "++pre"  => $"PreInc{pos}",
                    "--pre"  => $"PreDec{pos}",
                    "++post" => $"PostInc{pos}",
                    "--post" => $"PostDec{pos}",
                    _        => $"Unary '{u.Op}'{pos}"
                },
                BinaryExpr b      => $"Binary '{b.Op}'{pos}",

                _                 => n.GetType().Name + pos
            };
        }

        private static IReadOnlyList<(string edge, Node child)> Children(Node n, bool showPositions)
        {
            var list = new List<(string, Node)>();

            switch (n)
            {
                case TranslationUnit tu:
                    for (int i = 0; i < tu.Decls.Count; i++)
                        list.Add(($"decls[{i}]", tu.Decls[i]));
                    break;

                case FuncDef f:
                    for (int i = 0; i < f.Params.Count; i++)
                        list.Add(($"param[{i}]", f.Params[i]));
                    list.Add(("body", f.Body));
                    break;

                case CompoundStmt b:
                    for (int i = 0; i < b.Items.Count; i++)
                        list.Add(($"item[{i}]", b.Items[i]));
                    break;

                case VarDecl v:
                    if (v.Init is not null) list.Add(("init", v.Init));
                    break;

                case ReturnStmt r:
                    if (r.Expr is not null) list.Add(("expr", r.Expr));
                    break;

                case ExprStmt s:
                    if (s.Expr is not null) list.Add(("expr", s.Expr));
                    break;

                case IfStmt i:
                    list.Add(("cond", i.Cond));
                    list.Add(("then", i.Then));
                    if (i.Else is not null) list.Add(("else", i.Else));
                    break;

                case ForStmt f:
                    if (f.InitDecl is not null) list.Add(("init(decl)", f.InitDecl));
                    if (f.InitExpr is not null) list.Add(("init(expr)", f.InitExpr));
                    if (f.Cond is not null)     list.Add(("cond", f.Cond));
                    if (f.Post is not null)     list.Add(("post", f.Post));
                    list.Add(("body", f.Body));
                    break;

                case AssignExpr a:
                    list.Add(("lhs", a.Left));
                    list.Add(("rhs", a.Right));
                    break;

                case CallExpr c:
                    list.Add(("callee", c.Callee));
                    for (int i = 0; i < c.Args.Count; i++)
                        list.Add(($"arg[{i}]", c.Args[i]));
                    break;

                case UnaryExpr u:
                    list.Add(("expr", u.Expr));
                    break;

                case BinaryExpr b:
                    list.Add(("left", b.Left));
                    list.Add(("right", b.Right));
                    break;

                // leaf nodes: IntegerExpr, IdentExpr, ParamDecl (no children)
            }

            return list;
        }
    }
}
