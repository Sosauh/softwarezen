namespace Gpc.Engine.Syntax;

public enum TokenKind
{
    End, Identifier, Integer, Fixed, String, Char,
    // punctuation
    LParen, RParen, LBrace, RBrace, LBracket, RBracket, Comma, Semicolon, Colon, Question, Dot,
    // operators
    Plus, Minus, Star, Slash, Percent, Amp, Pipe, Caret, Tilde, Bang,
    Shl, Shr, Lt, Gt, Le, Ge, EqEq, NotEq, AmpAmp, PipePipe, PlusPlus, MinusMinus,
    Assign, PlusAssign, MinusAssign, StarAssign, SlashAssign, PercentAssign,
    AmpAssign, PipeAssign, CaretAssign, ShlAssign, ShrAssign,
}

/// <param name="Value">Integer value, or for <see cref="TokenKind.Fixed"/> the 16.16 fixed-point value.</param>
public readonly record struct Token(TokenKind Kind, string Text, int Value, SourceSpan Span)
{
    public override string ToString() => $"{Kind} '{Text}' @{Span}";
}
