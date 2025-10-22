using System.Text;

namespace MiniC;

public static class PpAstPrinter
{
    public static string Dump(PpTranslationUnit root, bool showTokenKinds = false, bool showSpans = false)
    {
        var sb = new StringBuilder();
        WriteNode(sb, prefix: "", isLast: true, Label(root, showTokenKinds, showSpans), Children(root, showTokenKinds, showSpans));
        return sb.ToString();
    }

    public static void DumpToConsole(PpTranslationUnit root, bool showTokenKinds = false, bool showSpans = false)
        => System.Console.WriteLine(Dump(root, showTokenKinds, showSpans));

    // ├── / └── style pretty tree
    private static void WriteNode(
        StringBuilder sb,
        string prefix,
        bool isLast,
        string label,
        IReadOnlyList<(string edge, object child)> children)
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

            // recurse: node line(s)
            var grandChildren = Children(child, showTokenKinds: label.Contains("⟨kinds⟩"), showSpans: label.Contains("⟨spans⟩"));
            WriteNode(sb,
                      childPrefix + (last ? "    " : "│   "),
                      true,
                      Label(child, label.Contains("⟨kinds⟩"), label.Contains("⟨spans⟩")),
                      grandChildren);
        }
    }

    // ---------- Labels ----------
    private static string Label(object node, bool showKinds, bool showSpans)
    {
        string optSuffix = (showKinds ? " ⟨kinds⟩" : "") + (showSpans ? " ⟨spans⟩" : "");

        switch (node)
        {
            case PpTranslationUnit _:
                return "PpTranslationUnit" + optSuffix;

            case PpText t:
                return $"Text: {RenderTokensOneLine(t.Tokens, showKinds, showSpans, maxLen: 120)}{optSuffix}";

            case PpIncludeDirective inc:
                return $"#include {RenderTokensOneLine(inc.Raw, showKinds, showSpans, maxLen: 200)}{optSuffix}";

            case PpDefineDirective d:
                {
                    var sig = d.IsFunctionLike
                        ? $"{d.Name}({string.Join(", ", d.Parameters)}{(d.IsVariadic ? (d.Parameters.Count > 0 ? ", ..." : "...") : "")})"
                        : d.Name;

                    var repl = d.Replacement is { Count: > 0 }
                        ? " " + RenderTokensOneLine(d.Replacement, showKinds, showSpans, maxLen: 200)
                        : "";

                    return $"#define {sig}{repl}{optSuffix}";
                }

            case PpUndefDirective u:
                return $"#undef {u.Name}{optSuffix}";

            case PpIfSection _:
                return "IfSection" + optSuffix;

            case PpIfLike iff:
                {
                    var head = iff.Kind switch
                    {
                        PpConditionKind.If => "#if ",
                        PpConditionKind.Ifdef => "#ifdef ",
                        PpConditionKind.Ifndef => "#ifndef ",
                        _ => "#if "
                    };
                    return $"{head}{RenderTokensOneLine(iff.ConditionTokens, showKinds, showSpans, maxLen: 200)}{optSuffix}";
                }

            case PpElif e:
                return $"#elif {RenderTokensOneLine(e.ConditionTokens, showKinds, showSpans, maxLen: 200)}{optSuffix}";

            case PpElse _:
                return "#else" + optSuffix;

            case PpSimpleDirective s:
                {
                    var rest = s.RestOfLine is { Count: > 0 }
                        ? " " + RenderTokensOneLine(s.RestOfLine, showKinds, showSpans, maxLen: 200)
                        : "";
                    return $"#{s.Keyword}{rest}{optSuffix}";
                }

            default:
                return node.GetType().Name + optSuffix;
        }
    }

    // ---------- Children ----------
    private static IReadOnlyList<(string edge, object child)> Children(object node, bool showTokenKinds, bool showSpans)
    {
        var list = new List<(string, object)>();

        switch (node)
        {
            case PpTranslationUnit tu:
                for (int i = 0; i < tu.Parts.Count; i++)
                    list.Add(($"part[{i}]", tu.Parts[i]));
                break;

            case PpIfSection s:
                list.Add(("if", s.If));
                for (int i = 0; i < s.Elifs.Count; i++)
                    list.Add(($"elif[{i}]", s.Elifs[i]));
                if (s.Else is not null)
                    list.Add(("else", s.Else));
                break;

            case PpIfLike iff:
                for (int i = 0; i < iff.Body.Count; i++)
                    list.Add(($"body[{i}]", iff.Body[i]));
                break;

            case PpElif e:
                for (int i = 0; i < e.Body.Count; i++)
                    list.Add(($"body[{i}]", e.Body[i]));
                break;

            case PpElse el:
                for (int i = 0; i < el.Body.Count; i++)
                    list.Add(($"body[{i}]", el.Body[i]));
                break;

                // leaves: PpText, PpIncludeDirective, PpDefineDirective, PpUndefDirective, PpSimpleDirective
        }

        return list;
    }

    // ---------- Token rendering helpers ----------
    private static string RenderTokensOneLine(IReadOnlyList<Token> toks, bool showKinds, bool showSpans, int maxLen)
    {
        if (toks == null || toks.Count == 0) return "(empty)";

        var sb = new StringBuilder();
        bool first = true;

        foreach (var t in toks)
        {
            if (!first && HasInterTokenSpace(t)) sb.Append(' ');
            first = false;

            if (showKinds) sb.Append('[').Append(t.Kind).Append(']');

            var text = t.Source.Src.AsSpan(t.Start, t.Length);
            if (maxLen > 0 && text.Length > maxLen) text = text[..maxLen];
            sb.Append(text);

            if (showSpans) sb.Append($"[{TryFileName(t)}:{t.Start}..{t.Start + t.Length}]");
        }

        return sb.ToString();
    }

    /// Inter-token space rule for PP: use the current token's leading trivia.
    private static bool HasInterTokenSpace(Token t)
    {
        var lead = t.Leading.Span;
        bool sawNewline = false, sawSpaceOrComment = false;
        for (int i = 0; i < lead.Length; i++)
        {
            switch (lead[i].Kind)
            {
                case TriviaKind.Newline: sawNewline = true; break;
                case TriviaKind.Space:
                case TriviaKind.LineComment:
                case TriviaKind.BlockComment:
                    sawSpaceOrComment = true; break;
            }
        }
        return !sawNewline && sawSpaceOrComment;
    }

    private static string TryFileName(Token t)
    {
        var src = t.Source;
        var nameProp = src.GetType().GetProperty("FileName");
        if (nameProp?.GetValue(src) is string s && !string.IsNullOrEmpty(s))
            return s;
        return "file";
    }
}