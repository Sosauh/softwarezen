using System.Text;

namespace Gpc.Engine.Syntax;

public sealed class Lexer
{
    readonly string _src;
    readonly DiagnosticBag _diag;
    int _pos, _line = 1, _lineStart;

    public Lexer(string source, DiagnosticBag diagnostics)
    {
        _src = source;
        _diag = diagnostics;
    }

    public static List<Token> Tokenize(string source, DiagnosticBag diagnostics)
    {
        var lx = new Lexer(source, diagnostics);
        var list = new List<Token>();
        Token t;
        do list.Add(t = lx.Next()); while (t.Kind != TokenKind.End);
        return list;
    }

    char Peek(int o = 0) => _pos + o < _src.Length ? _src[_pos + o] : '\0';

    SourceSpan SpanFrom(int start, int line, int col) => new(start, _pos - start, line, col);

    void SkipTrivia()
    {
        while (_pos < _src.Length)
        {
            char c = Peek();
            if (c == '\n') { _pos++; _line++; _lineStart = _pos; }
            else if (char.IsWhiteSpace(c)) _pos++;
            else if (c == '/' && Peek(1) == '/') { while (_pos < _src.Length && Peek() != '\n') _pos++; }
            else if (c == '/' && Peek(1) == '*')
            {
                int start = _pos, line = _line, col = _pos - _lineStart + 1;
                _pos += 2;
                while (_pos < _src.Length && !(Peek() == '*' && Peek(1) == '/'))
                {
                    if (Peek() == '\n') { _line++; _lineStart = _pos + 1; }
                    _pos++;
                }
                if (_pos >= _src.Length) _diag.Error("GZ1001", "unterminated block comment", SpanFrom(start, line, col));
                else _pos += 2;
            }
            else break;
        }
    }

    public Token Next()
    {
        SkipTrivia();
        int start = _pos, line = _line, col = _pos - _lineStart + 1;
        if (_pos >= _src.Length) return new(TokenKind.End, "", 0, new(start, 0, line, col));

        char c = Peek();
        if (char.IsLetter(c) || c == '_')
        {
            while (char.IsLetterOrDigit(Peek()) || Peek() == '_') _pos++;
            return new(TokenKind.Identifier, _src[start.._pos], 0, SpanFrom(start, line, col));
        }
        if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1))))
            return Number(start, line, col);
        if (c == '"') return StringLit(start, line, col);
        if (c == '\'' && _pos + 2 < _src.Length) return CharLit(start, line, col);

        _pos++;
        TokenKind k = c switch
        {
            '(' => TokenKind.LParen, ')' => TokenKind.RParen, '{' => TokenKind.LBrace, '}' => TokenKind.RBrace,
            '[' => TokenKind.LBracket, ']' => TokenKind.RBracket, ',' => TokenKind.Comma, ';' => TokenKind.Semicolon,
            ':' => TokenKind.Colon, '?' => TokenKind.Question, '.' => TokenKind.Dot, '~' => TokenKind.Tilde,
            '+' => Match('+') ? TokenKind.PlusPlus : Match('=') ? TokenKind.PlusAssign : TokenKind.Plus,
            '-' => Match('-') ? TokenKind.MinusMinus : Match('=') ? TokenKind.MinusAssign : Match('>') ? TokenKind.Arrow : TokenKind.Minus,
            '*' => Match('=') ? TokenKind.StarAssign : TokenKind.Star,
            '/' => Match('=') ? TokenKind.SlashAssign : TokenKind.Slash,
            '%' => Match('=') ? TokenKind.PercentAssign : TokenKind.Percent,
            '^' => Match('^') ? (Match('=') ? TokenKind.CaretCaretAssign : TokenKind.CaretCaret) : Match('=') ? TokenKind.CaretAssign : TokenKind.Caret,
            '&' => Match('&') ? (Match('=') ? TokenKind.AmpAmpAssign : TokenKind.AmpAmp) : Match('=') ? TokenKind.AmpAssign : TokenKind.Amp,
            '|' => Match('|') ? (Match('=') ? TokenKind.PipePipeAssign : TokenKind.PipePipe) : Match('=') ? TokenKind.PipeAssign : TokenKind.Pipe,
            '!' => Match('=') ? TokenKind.NotEq : TokenKind.Bang,
            '=' => Match('=') ? TokenKind.EqEq : TokenKind.Assign,
            '<' => Match('<') ? (Match('=') ? TokenKind.ShlAssign : TokenKind.Shl) : Match('=') ? TokenKind.Le : TokenKind.Lt,
            '>' => Match('>') ? (Match('=') ? TokenKind.ShrAssign : TokenKind.Shr) : Match('=') ? TokenKind.Ge : TokenKind.Gt,
            _ => TokenKind.End,
        };
        if (k == TokenKind.End)
        {
            // the Zen compiler skips stray characters with a warning
            _diag.Warning("GZ1002", $"stray character '{c}' ignored", SpanFrom(start, line, col));
            return Next();
        }
        return new(k, _src[start.._pos], 0, SpanFrom(start, line, col));
    }

    bool Match(char c)
    {
        if (Peek() != c) return false;
        _pos++;
        return true;
    }

    Token Number(int start, int line, int col)
    {
        if (Peek() == '0' && (Peek(1) is 'x' or 'X' or 'b' or 'B'))
        {
            int radix = Peek(1) is 'x' or 'X' ? 16 : 2;
            _pos += 2;
            int digitsStart = _pos;
            while (Uri.IsHexDigit(Peek()) || Peek() == '_') _pos++;
            string digits = _src[digitsStart.._pos].Replace("_", "");
            ulong v = 0;
            bool ok = digits.Length > 0;
            foreach (char d in digits)
            {
                int dv = Convert.ToInt32(d.ToString(), 16);
                if (dv >= radix) { ok = false; break; }
                v = v * (ulong)radix + (ulong)dv;
                if (v > uint.MaxValue) { ok = false; break; }
            }
            var span = SpanFrom(start, line, col);
            if (!ok) _diag.Error("GZ1003", $"invalid {(radix == 16 ? "hex" : "binary")} literal '{_src[start.._pos]}'", span);
            return new(TokenKind.Integer, _src[start.._pos], unchecked((int)(uint)v), span);
        }

        while (char.IsDigit(Peek())) _pos++;
        bool isFraction = false;
        if (Peek() == '.' && char.IsDigit(Peek(1)))
        {
            isFraction = true;
            _pos++;
            while (char.IsDigit(Peek())) _pos++;
        }
        if (Peek() is 'e' or 'E')
        {
            isFraction = true;
            _pos++;
            if (Peek() is '+' or '-') _pos++;
            if (!char.IsDigit(Peek()))
                _diag.Error("GZ1007", "exponent has no digits", SpanFrom(start, line, col));
            while (char.IsDigit(Peek())) _pos++;
        }
        string text = _src[start.._pos];
        var sp = SpanFrom(start, line, col);
        if (isFraction)
        {
            // GPC has no float type: the scanner rounds to an integer and warns
            double d = double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            double r = Math.Round(d, MidpointRounding.AwayFromZero);
            _diag.Warning("GZ1008", $"decimal literal '{text}' rounded to {r}", sp);
            return new(TokenKind.Integer, text, unchecked((int)(long)Math.Clamp(r, long.MinValue, long.MaxValue)), sp);
        }

        if (!long.TryParse(text, out long value) || value > int.MaxValue)
        {
            _diag.Error("GZ1004", $"decimal literal '{text}' exceeds 2147483647", sp);
            value = 0;
        }
        return new(TokenKind.Integer, text, (int)value, sp);
    }

    Token StringLit(int start, int line, int col)
    {
        _pos++;
        var sb = new StringBuilder();
        while (_pos < _src.Length && Peek() != '"' && Peek() != '\n')
            sb.Append(ReadCharInLiteral());
        if (Peek() != '"') _diag.Error("GZ1005", "unterminated string literal", SpanFrom(start, line, col));
        else _pos++;
        return new(TokenKind.String, sb.ToString(), 0, SpanFrom(start, line, col));
    }

    /// <summary>'A' is the number 65. Not in the guide, but common in scripts, so accepted.</summary>
    Token CharLit(int start, int line, int col)
    {
        _pos++;
        int v = Peek() is '\'' or '\n' ? 0 : ReadCharInLiteral();
        if (Peek() != '\'') _diag.Error("GZ1006", "unterminated character literal", SpanFrom(start, line, col));
        else _pos++;
        return new(TokenKind.Integer, _src[start.._pos], v, SpanFrom(start, line, col));
    }

    char ReadCharInLiteral()
    {
        char c = _src[_pos++];
        if (c != '\\' || _pos >= _src.Length) return c;
        char e = _src[_pos++];
        switch (e)
        {
            case 'n': return '\n';
            case 't': return '\t';
            case 'r': return '\r';
            case '0': return '\0';
            case '\\': case '"': case '\'': return e;
            case 'x':
                if (_pos + 1 < _src.Length && Uri.IsHexDigit(_src[_pos]) && Uri.IsHexDigit(_src[_pos + 1]))
                {
                    char v = (char)Convert.ToInt32(_src.Substring(_pos, 2), 16);
                    _pos += 2;
                    return v;
                }
                _diag.Error("GZ1009", "\\x needs exactly two hex digits", new(_pos - 2, 2, _line, _pos - _lineStart - 1));
                return 'x';
            default:
                _diag.Error("GZ1010", $"unknown escape '\\{e}'", new(_pos - 2, 2, _line, _pos - _lineStart - 1));
                return e;
        }
    }
}
