namespace Gpc.Engine;

public enum Severity { Warning, Error }

public readonly record struct SourceSpan(int Start, int Length, int Line, int Column)
{
    public override string ToString() => $"{Line}:{Column}";
}

public sealed record Diagnostic(Severity Severity, string Code, string Message, SourceSpan Span)
{
    public override string ToString() => $"{Span}: {(Severity == Severity.Error ? "error" : "warning")} {Code}: {Message}";
}

public sealed class DiagnosticBag
{
    readonly List<Diagnostic> _items = new();
    public IReadOnlyList<Diagnostic> Items => _items;
    public bool HasErrors => _items.Any(d => d.Severity == Severity.Error);

    public void Error(string code, string message, SourceSpan span) => _items.Add(new(Severity.Error, code, message, span));
    public void Warning(string code, string message, SourceSpan span) => _items.Add(new(Severity.Warning, code, message, span));
}
