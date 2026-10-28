namespace Gpc.Engine.Syntax;

/// <summary>
/// Recursive-descent parser for Zen 32-bit GPC. Reports several errors per file: after an
/// error it skips to the next ';' or '}' (statements) or the next top-level keyword.
/// Missing semicolons are accepted with a warning, as the Zen compiler tolerates most of them.
/// </summary>
public sealed class Parser
{
    static readonly HashSet<string> TopLevelKeywords = new()
    {
        "define", "enum", "data", "const", "int", "init", "main", "combo", "fcombo", "function", "remap", "unmap",
    };
    public static readonly HashSet<string> ConstElementTypes = new()
    {
        "int8", "uint8", "int16", "uint16", "int24", "uint24", "int32", "string", "image", "ps5adt",
        "int", "char", "byte", // legacy, deprecated
    };
    public static readonly HashSet<string> SizeofTypes = new() { "int8", "uint8", "int16", "uint16", "int24", "uint24", "int32" };

    const int MaxDepth = 200;

    readonly List<Token> _tokens;
    readonly DiagnosticBag _diag;
    int _pos, _depth;

    sealed class ParseAbort : Exception { }

    public Parser(List<Token> tokens, DiagnosticBag diagnostics)
    {
        _tokens = tokens;
        _diag = diagnostics;
    }

    public static CompilationUnit Parse(string source, DiagnosticBag diagnostics) =>
        new Parser(Lexer.Tokenize(source, diagnostics), diagnostics).ParseUnit();

    Token Cur => _tokens[_pos];
    Token PeekTok(int o = 1) => _tokens[Math.Min(_pos + o, _tokens.Count - 1)];
    bool At(TokenKind k) => Cur.Kind == k;
    bool AtWord(string w) => Cur.Kind == TokenKind.Identifier && Cur.Text == w;

    Token Advance()
    {
        var t = Cur;
        if (t.Kind != TokenKind.End) _pos++;
        return t;
    }

    bool Accept(TokenKind k)
    {
        if (!At(k)) return false;
        _pos++;
        return true;
    }

    Token Expect(TokenKind k, string code, string what)
    {
        if (At(k)) return Advance();
        Error(code, $"expected {what} but found {Describe(Cur)}");
        throw new ParseAbort();
    }

    string ExpectName(string code, string what)
    {
        if (At(TokenKind.Identifier)) return Advance().Text;
        Error(code, $"expected {what} but found {Describe(Cur)}");
        throw new ParseAbort();
    }

    void ExpectSemicolon()
    {
        if (Accept(TokenKind.Semicolon)) return;
        var prev = _tokens[Math.Max(_pos - 1, 0)];
        _diag.Warning("GZ2108", "missing ';'", prev.Span);
    }

    static string Describe(Token t) => t.Kind switch
    {
        TokenKind.End => "end of file",
        TokenKind.Identifier => $"'{t.Text}'",
        TokenKind.Integer => $"number {t.Text}",
        TokenKind.String => "string literal",
        _ => $"'{t.Text}'",
    };

    void Error(string code, string message) => _diag.Error(code, message, Cur.Span);

    static SourceSpan Join(SourceSpan a, SourceSpan b) =>
        new(a.Start, Math.Max(a.Length, b.Start + b.Length - a.Start), a.Line, a.Column);

    SourceSpan From(SourceSpan start) => Join(start, _tokens[Math.Max(_pos - 1, 0)].Span);

    // ================= top level =================

    public CompilationUnit ParseUnit()
    {
        var decls = new List<Decl>();
        while (!At(TokenKind.End))
        {
            int before = _pos;
            try
            {
                var d = ParseDecl();
                if (d != null) decls.Add(d);
            }
            catch (ParseAbort)
            {
                SyncTopLevel(before);
            }
        }
        return new CompilationUnit(decls);
    }

    void SyncTopLevel(int startPos)
    {
        if (_pos == startPos) Advance();
        int braces = 0;
        while (!At(TokenKind.End))
        {
            if (At(TokenKind.LBrace)) braces++;
            else if (At(TokenKind.RBrace)) { braces--; if (braces <= 0) { Advance(); if (braces == 0) return; braces = 0; continue; } }
            else if (braces <= 0 && At(TokenKind.Semicolon)) { Advance(); return; }
            else if (braces <= 0 && Cur.Kind == TokenKind.Identifier && TopLevelKeywords.Contains(Cur.Text)
                     && (_pos == 0 || _tokens[_pos - 1].Kind is TokenKind.Semicolon or TokenKind.RBrace)) return;
            Advance();
        }
    }

    Decl? ParseDecl()
    {
        var start = Cur.Span;
        if (Accept(TokenKind.Semicolon)) return null;
        if (Cur.Kind != TokenKind.Identifier)
        {
            Error("GZ2103", $"expected a declaration but found {Describe(Cur)}");
            throw new ParseAbort();
        }
        switch (Cur.Text)
        {
            case "define":
            {
                Advance();
                string name = ExpectName("GZ2115", "a name after 'define'");
                Expect(TokenKind.Assign, "GZ2114", "'='");
                var value = ParseExpr();
                ExpectSemicolon();
                return new DefineDecl(name, value, From(start));
            }
            case "enum":
            {
                Advance();
                Expect(TokenKind.LBrace, "GZ2113", "'{'");
                var members = new List<EnumMember>();
                while (!At(TokenKind.RBrace))
                {
                    var ms = Cur.Span;
                    string name = ExpectName("GZ2115", "an enum member name");
                    Expr? v = Accept(TokenKind.Assign) ? ParseExpr() : null;
                    members.Add(new EnumMember(name, v, From(ms)));
                    if (!Accept(TokenKind.Comma)) break;
                    if (At(TokenKind.RBrace)) _diag.Error("GZ2101", "trailing comma in enum", _tokens[_pos - 1].Span);
                }
                Expect(TokenKind.RBrace, "GZ2106", "'}'");
                ExpectSemicolon();
                return new EnumDecl(members, From(start));
            }
            case "data":
            {
                Advance();
                if (At(TokenKind.LBrace)) { Error("GZ2140", "data uses parentheses, not braces"); throw new ParseAbort(); }
                Expect(TokenKind.LParen, "GZ2111", "'('");
                var values = new List<Expr>();
                if (At(TokenKind.RParen)) Error("GZ2141", "data() needs at least one value");
                while (!At(TokenKind.RParen))
                {
                    values.Add(ParseTernary());
                    if (!Accept(TokenKind.Comma)) break;
                    if (At(TokenKind.RParen)) _diag.Error("GZ2101", "trailing comma in data()", _tokens[_pos - 1].Span);
                }
                Expect(TokenKind.RParen, "GZ2104", "')'");
                ExpectSemicolon();
                return new DataDecl(values, From(start));
            }
            case "const":
                return ParseConst(start);
            case "int":
                return ParseVarDecl(start);
            case "init":
            case "main":
            {
                bool isInit = Advance().Text == "init";
                var body = ParseBlockRequired();
                return new EntryDecl(isInit, body, From(start));
            }
            case "combo":
            case "fcombo":
            {
                bool isF = Advance().Text == "fcombo";
                string name = ExpectName("GZ2115", "a combo name");
                var body = ParseBlockRequired();
                return new ComboDecl(name, isF, body, From(start));
            }
            case "function":
            {
                Advance();
                string name = ExpectName("GZ2115", "a function name");
                Expect(TokenKind.LParen, "GZ2111", "'('");
                var ps = new List<string>();
                while (!At(TokenKind.RParen))
                {
                    if (AtWord("int") && PeekTok().Kind == TokenKind.Identifier) Advance(); // tolerated
                    ps.Add(ExpectName("GZ2181", "a parameter name"));
                    if (!Accept(TokenKind.Comma)) break;
                }
                Expect(TokenKind.RParen, "GZ2180", "')' to close the parameter list");
                var body = ParseBlockRequired();
                return new FunctionDecl(name, ps, body, From(start));
            }
            case "remap":
            {
                Advance();
                var src = ParseTernary();
                Expect(TokenKind.Arrow, "GZ2100", "'->'");
                var dst = ParseTernary();
                ExpectSemicolon();
                return new RemapDecl(src, dst, From(start));
            }
            case "unmap":
            {
                Advance();
                var src = ParseTernary();
                ExpectSemicolon();
                return new RemapDecl(src, null, From(start));
            }
            default:
                Error("GZ2103", $"executable code is not allowed at top level ('{Cur.Text}'); put it inside main, init, a combo or a function");
                throw new ParseAbort();
        }
    }

    Decl ParseConst(SourceSpan start)
    {
        Advance(); // const
        string type = ExpectName("GZ2131", "a const element type");
        if (!ConstElementTypes.Contains(type)) _diag.Error("GZ2131", $"'{type}' is not a const element type", _tokens[_pos - 1].Span);
        string name = ExpectName("GZ2115", "a const name");
        int dims = 0;
        while (Accept(TokenKind.LBracket))
        {
            if (!At(TokenKind.RBracket))
            {
                Error("GZ2130", "const arrays take their size from the initializer; leave the brackets empty");
                ParseExpr();
            }
            Expect(TokenKind.RBracket, "GZ2105", "']'");
            dims++;
        }
        Expect(TokenKind.Assign, "GZ2114", "'='");
        var init = ParseConstInit();
        ExpectSemicolon();
        return new ConstDecl(type, name, dims, init, From(start));
    }

    ConstInit ParseConstInit()
    {
        var start = Cur.Span;
        if (At(TokenKind.String)) return new ConstString(Advance().Text, start);
        if (Accept(TokenKind.LBrace))
        {
            var items = new List<ConstInit>();
            while (!At(TokenKind.RBrace))
            {
                items.Add(ParseConstInit());
                if (!Accept(TokenKind.Comma)) break;
                if (At(TokenKind.RBrace)) _diag.Error("GZ2101", "trailing comma in const array", _tokens[_pos - 1].Span);
            }
            Expect(TokenKind.RBrace, "GZ2106", "'}'");
            return new ConstList(items, From(start));
        }
        return new ConstValue(ParseTernary(), From(start));
    }

    VarDecl ParseVarDecl(SourceSpan start)
    {
        Advance(); // int
        var items = new List<VarItem>();
        do
        {
            var s = Cur.Span;
            string name = ExpectName("GZ2150", "a variable name");
            Expr? size = null, init = null;
            if (Accept(TokenKind.LBracket))
            {
                size = ParseExpr();
                Expect(TokenKind.RBracket, "GZ2105", "']'");
            }
            if (Accept(TokenKind.Assign))
            {
                init = ParseTernary();
                if (size != null) _diag.Error("GZ2100", "runtime arrays cannot have initializers", init.Span);
            }
            items.Add(new VarItem(name, size, init, From(s)));
        } while (Accept(TokenKind.Comma));
        ExpectSemicolon();
        return new VarDecl(items, From(start));
    }

    // ================= statements =================

    Block ParseBlockRequired()
    {
        if (!At(TokenKind.LBrace))
        {
            Error("GZ2113", $"expected '{{' but found {Describe(Cur)}");
            throw new ParseAbort();
        }
        return ParseBlock();
    }

    Block ParseBlock()
    {
        var start = Expect(TokenKind.LBrace, "GZ2113", "'{'").Span;
        var stmts = new List<Stmt>();
        while (!At(TokenKind.RBrace) && !At(TokenKind.End))
        {
            int before = _pos;
            try
            {
                stmts.Add(ParseStatement());
            }
            catch (ParseAbort)
            {
                SyncStatement(before);
            }
        }
        Expect(TokenKind.RBrace, "GZ2120", "'}' to close the block");
        return new Block(stmts, From(start));
    }

    void SyncStatement(int startPos)
    {
        if (_pos == startPos && !At(TokenKind.RBrace)) Advance();
        while (!At(TokenKind.End) && !At(TokenKind.RBrace))
        {
            if (Accept(TokenKind.Semicolon)) return;
            if (At(TokenKind.LBrace)) { SkipBalanced(); return; }
            Advance();
        }
    }

    void SkipBalanced()
    {
        int depth = 0;
        do
        {
            if (At(TokenKind.LBrace)) depth++;
            else if (At(TokenKind.RBrace)) depth--;
            Advance();
        } while (depth > 0 && !At(TokenKind.End));
    }

    Stmt ParseStatement()
    {
        if (++_depth > MaxDepth)
        {
            _depth = 0;
            Error("GZ2255", "nesting is deeper than 200 levels");
            throw new ParseAbort();
        }
        try { return ParseStatementInner(); }
        finally { _depth--; }
    }

    Stmt ParseStatementInner()
    {
        var start = Cur.Span;
        if (At(TokenKind.LBrace)) return ParseBlock();
        if (Accept(TokenKind.Semicolon)) return new EmptyStmt(start);

        if (Cur.Kind == TokenKind.Identifier)
        {
            switch (Cur.Text)
            {
                case "if":
                {
                    Advance();
                    var cond = ParseParenCondition("GZ2200", "GZ2201");
                    var then = ParseStatement();
                    Stmt? els = null;
                    if (AtWord("else")) { Advance(); els = ParseStatement(); }
                    return new IfStmt(cond, then, els, From(start));
                }
                case "while":
                {
                    Advance();
                    var cond = ParseParenCondition("GZ2111", "GZ2104");
                    var body = ParseStatement();
                    return new WhileStmt(cond, body, From(start));
                }
                case "do":
                {
                    Advance();
                    var body = ParseStatement();
                    if (!AtWord("while")) { Error("GZ2240", "'do' needs a matching 'while'"); throw new ParseAbort(); }
                    Advance();
                    var cond = ParseParenCondition("GZ2111", "GZ2104");
                    ExpectSemicolon();
                    return new DoWhileStmt(body, cond, From(start));
                }
                case "for":
                {
                    Advance();
                    Expect(TokenKind.LParen, "GZ2250", "'(' after 'for'");
                    if (AtWord("int")) { Error("GZ4013", "variables cannot be declared in a for header; declare them globally"); throw new ParseAbort(); }
                    Expr? init = At(TokenKind.Semicolon) ? null : ParseExpr();
                    Expect(TokenKind.Semicolon, "GZ2251", "';' after the for initializer");
                    Expr? cond = At(TokenKind.Semicolon) ? null : ParseExpr();
                    Expect(TokenKind.Semicolon, "GZ2252", "';' after the for condition");
                    Expr? step = At(TokenKind.RParen) ? null : ParseExpr();
                    Expect(TokenKind.RParen, "GZ2104", "')'");
                    var body = ParseStatement();
                    return new ForStmt(init, cond, step, body, From(start));
                }
                case "switch":
                    return ParseSwitch(start);
                case "break":
                    Advance(); ExpectSemicolon(); return new BreakStmt(From(start));
                case "continue":
                    Advance(); ExpectSemicolon(); return new ContinueStmt(From(start));
                case "return":
                {
                    Advance();
                    Expr? v = At(TokenKind.Semicolon) || At(TokenKind.RBrace) ? null : ParseExpr();
                    ExpectSemicolon();
                    return new ReturnStmt(v, From(start));
                }
                case "wait":
                    if (PeekTok().Kind == TokenKind.LParen)
                    {
                        Advance();
                        Expect(TokenKind.LParen, "GZ2111", "'('");
                        var d = ParseExpr();
                        Expect(TokenKind.RParen, "GZ2104", "')'");
                        ExpectSemicolon();
                        return new WaitStmt(d, From(start));
                    }
                    break;
                case "call":
                    if (PeekTok().Kind == TokenKind.LParen)
                    {
                        Advance();
                        Expect(TokenKind.LParen, "GZ2111", "'('");
                        string name = ExpectName("GZ4009", "a combo name");
                        Expect(TokenKind.RParen, "GZ2104", "')'");
                        ExpectSemicolon();
                        return new CallComboStmt(name, From(start));
                    }
                    break;
                case "int":
                    if (PeekTok().Kind == TokenKind.Identifier)
                    {
                        Error("GZ4003", "variables must be declared at top level, not inside a block");
                        throw new ParseAbort();
                    }
                    break;
                case "else":
                    Error("GZ2103", "'else' without a matching 'if'");
                    throw new ParseAbort();
            }
        }

        var e = ParseExpr();
        ExpectSemicolon();
        return new ExprStmt(e, From(start));
    }

    Expr ParseParenCondition(string openCode, string closeCode)
    {
        Expect(TokenKind.LParen, openCode, "'('");
        var e = ParseExpr();
        Expect(TokenKind.RParen, closeCode, "')'");
        return e;
    }

    Stmt ParseSwitch(SourceSpan start)
    {
        Advance();
        var value = ParseParenCondition("GZ2111", "GZ2104");
        Expect(TokenKind.LBrace, "GZ2113", "'{'");
        var cases = new List<SwitchCase>();
        while (!At(TokenKind.RBrace) && !At(TokenKind.End))
        {
            var cs = Cur.Span;
            Expr? label;
            if (AtWord("case")) { Advance(); label = ParseTernary(); }
            else if (AtWord("default")) { Advance(); label = null; }
            else { Error("GZ2103", $"expected 'case' or 'default' but found {Describe(Cur)}"); throw new ParseAbort(); }
            if (!Accept(TokenKind.Colon))
            {
                // scripts in the wild write "case 1 { ... }"; the Zen compiler takes it
                if (At(TokenKind.LBrace)) _diag.Warning("GZ2109", "missing ':' after the case label", Cur.Span);
                else { Error("GZ2100", $"expected ':' after the case label but found {Describe(Cur)}"); throw new ParseAbort(); }
            }
            // The body runs to the next case/default or the closing '}': a braced block, bare
            // statements ("case 1: x = 1; break;"), or nothing ("case 1: case 2: { ... }" shares a body).
            var bodyStart = Cur.Span;
            var stmts = new List<Stmt>();
            while (!AtWord("case") && !AtWord("default") && !At(TokenKind.RBrace) && !At(TokenKind.End))
            {
                int before = _pos;
                try
                {
                    stmts.Add(ParseStatement());
                }
                catch (ParseAbort)
                {
                    SyncStatement(before);
                }
            }
            cases.Add(new SwitchCase(label, new Block(stmts, From(bodyStart)), From(cs)));
        }
        Expect(TokenKind.RBrace, "GZ2106", "'}'");
        return new SwitchStmt(value, cases, From(start));
    }

    // ================= expressions =================

    public Expr ParseExpr() => ParseAssignment();

    static bool IsAssignOp(TokenKind k) => k is TokenKind.Assign or TokenKind.PlusAssign or TokenKind.MinusAssign
        or TokenKind.StarAssign or TokenKind.SlashAssign or TokenKind.PercentAssign or TokenKind.AmpAssign
        or TokenKind.PipeAssign or TokenKind.CaretAssign or TokenKind.ShlAssign or TokenKind.ShrAssign
        or TokenKind.AmpAmpAssign or TokenKind.PipePipeAssign or TokenKind.CaretCaretAssign;

    Expr ParseAssignment()
    {
        var left = ParseTernary();
        if (IsAssignOp(Cur.Kind))
        {
            var op = Advance().Kind;
            var right = ParseAssignment();
            return new AssignExpr(op, left, right, Join(left.Span, right.Span));
        }
        return left;
    }

    Expr ParseTernary()
    {
        var cond = ParseBinary(0);
        if (!Accept(TokenKind.Question)) return cond;
        var a = ParseAssignment();
        Expect(TokenKind.Colon, "GZ2117", "':' in the conditional expression");
        var b = ParseTernary();
        return new TernaryExpr(cond, a, b, Join(cond.Span, b.Span));
    }

    static readonly TokenKind[][] Levels =
    {
        new[] { TokenKind.PipePipe },
        new[] { TokenKind.CaretCaret },
        new[] { TokenKind.AmpAmp },
        new[] { TokenKind.Pipe },
        new[] { TokenKind.Caret },
        new[] { TokenKind.Amp },
        new[] { TokenKind.EqEq, TokenKind.NotEq },
        new[] { TokenKind.Lt, TokenKind.Le, TokenKind.Gt, TokenKind.Ge },
        new[] { TokenKind.Shl, TokenKind.Shr },
        new[] { TokenKind.Plus, TokenKind.Minus },
        new[] { TokenKind.Star, TokenKind.Slash, TokenKind.Percent },
    };

    Expr ParseBinary(int level)
    {
        if (level == Levels.Length) return ParseUnary();
        var left = ParseBinary(level + 1);
        while (Array.IndexOf(Levels[level], Cur.Kind) >= 0)
        {
            var op = Advance().Kind;
            var right = ParseBinary(level + 1);
            left = new BinaryExpr(op, left, right, Join(left.Span, right.Span));
        }
        return left;
    }

    Expr ParseUnary()
    {
        var start = Cur.Span;
        if (++_depth > MaxDepth)
        {
            _depth = 0;
            Error("GZ2255", "expression nesting is deeper than 200 levels");
            throw new ParseAbort();
        }
        try
        {
            switch (Cur.Kind)
            {
                case TokenKind.Minus:
                case TokenKind.Bang:
                case TokenKind.Tilde:
                case TokenKind.Plus:
                {
                    var op = Advance().Kind;
                    var operand = ParseUnary();
                    if (op == TokenKind.Plus) return operand;
                    return new UnaryExpr(op, operand, Join(start, operand.Span));
                }
                case TokenKind.PlusPlus:
                case TokenKind.MinusMinus:
                {
                    bool inc = Advance().Kind == TokenKind.PlusPlus;
                    var target = ParseUnary();
                    return new IncDecExpr(true, inc, target, Join(start, target.Span));
                }
                default:
                    return ParsePostfix();
            }
        }
        finally { _depth--; }
    }

    Expr ParsePostfix()
    {
        var e = ParsePrimary();
        while (true)
        {
            if (Accept(TokenKind.LBracket))
            {
                var idx = ParseExpr();
                Expect(TokenKind.RBracket, "GZ2105", "']'");
                e = new IndexExpr(e, idx, From(e.Span));
            }
            else if (At(TokenKind.PlusPlus) || At(TokenKind.MinusMinus))
            {
                bool inc = Advance().Kind == TokenKind.PlusPlus;
                e = new IncDecExpr(false, inc, e, From(e.Span));
            }
            else return e;
        }
    }

    Expr ParsePrimary()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case TokenKind.Integer:
                Advance();
                return new IntLit(t.Value, t.Span);
            case TokenKind.String:
                Advance();
                return new StringLit(t.Text, t.Span);
            case TokenKind.LParen:
            {
                Advance();
                var e = ParseExpr();
                Expect(TokenKind.RParen, "GZ2104", "')'");
                return e;
            }
            case TokenKind.Identifier:
            {
                Advance();
                if (t.Text == "sizeof")
                {
                    Expect(TokenKind.LParen, "GZ2111", "'(' after sizeof");
                    if (Cur.Kind == TokenKind.Identifier && SizeofTypes.Contains(Cur.Text) && PeekTok().Kind == TokenKind.RParen)
                    {
                        string type = Advance().Text;
                        Advance();
                        return new SizeofExpr(type, null, From(t.Span));
                    }
                    var operand = ParseExpr();
                    Expect(TokenKind.RParen, "GZ2104", "')'");
                    return new SizeofExpr(null, operand, From(t.Span));
                }
                if (t.Text == "addr")
                {
                    Expect(TokenKind.LParen, "GZ2111", "'(' after addr");
                    string name = ExpectName("GZ5126", "a const item name");
                    Expect(TokenKind.RParen, "GZ2104", "')'");
                    return new AddrExpr(name, From(t.Span));
                }
                if (Accept(TokenKind.LParen))
                {
                    var args = new List<Expr>();
                    while (!At(TokenKind.RParen))
                    {
                        args.Add(ParseAssignment());
                        if (!Accept(TokenKind.Comma)) break;
                    }
                    Expect(TokenKind.RParen, "GZ2104", "')' to close the argument list");
                    return new CallExpr(t.Text, args, From(t.Span));
                }
                return new NameExpr(t.Text, t.Span);
            }
            default:
                Error("GZ2102", $"expected an expression but found {Describe(t)}");
                throw new ParseAbort();
        }
    }
}
