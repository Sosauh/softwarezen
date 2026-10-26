namespace Gpc.Engine.Syntax;

public enum TokenKind
{
    End, Identifier, Integer, String,
    // punctuation
    LParen, RParen, LBrace, RBrace, LBracket, RBracket, Comma, Semicolon, Colon, Question, Dot,
    // operators
    Plus, Minus, Star, Slash, Percent, Amp, Pipe, Caret, Tilde, Bang,
    Shl, Shr, Lt, Gt, Le, Ge, EqEq, NotEq, AmpAmp, PipePipe, CaretCaret, PlusPlus, MinusMinus, Arrow,
    Assign, PlusAssign, MinusAssign, StarAssign, SlashAssign, PercentAssign,
    AmpAssign, PipeAssign, CaretAssign, ShlAssign, ShrAssign,
    AmpAmpAssign, PipePipeAssign, CaretCaretAssign,
}

public readonly record struct Token(TokenKind Kind, string Text, int Value, SourceSpan Span)
{
    public override string ToString() => $"{Kind} '{Text}' @{Span}";
}
