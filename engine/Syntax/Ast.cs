namespace Gpc.Engine.Syntax;

public abstract record Node(SourceSpan Span);

// ---- top level ----
public abstract record Decl(SourceSpan Span) : Node(Span);
public sealed record DefineDecl(string Name, Expr Value, SourceSpan Span) : Decl(Span);
public sealed record EnumMember(string Name, Expr? Value, SourceSpan Span);
public sealed record EnumDecl(List<EnumMember> Members, SourceSpan Span) : Decl(Span);
public sealed record DataDecl(List<Expr> Values, SourceSpan Span) : Decl(Span);

/// <summary>Const initializer: a scalar list (1D), a list of rows (2D), or a string.</summary>
public abstract record ConstInit(SourceSpan Span);
public sealed record ConstList(List<ConstInit> Items, SourceSpan Span) : ConstInit(Span);
public sealed record ConstValue(Expr Value, SourceSpan Span) : ConstInit(Span);
public sealed record ConstString(string Value, SourceSpan Span) : ConstInit(Span);
public sealed record ConstDecl(string ElementType, string Name, int Dimensions, ConstInit Init, SourceSpan Span) : Decl(Span);

public sealed record VarItem(string Name, Expr? ArraySize, Expr? Init, SourceSpan Span);
public sealed record VarDecl(List<VarItem> Items, SourceSpan Span) : Decl(Span);
public sealed record EntryDecl(bool IsInit, Block Body, SourceSpan Span) : Decl(Span);
public sealed record ComboDecl(string Name, bool IsFcombo, Block Body, SourceSpan Span) : Decl(Span);
public sealed record FunctionDecl(string Name, List<string> Parameters, Block Body, SourceSpan Span) : Decl(Span);
/// <summary><c>remap Source -> Target;</c>, or <c>unmap Source;</c> when Target is null.</summary>
public sealed record RemapDecl(Expr Source, Expr? Target, SourceSpan Span) : Decl(Span);

public sealed record CompilationUnit(List<Decl> Decls);

// ---- statements ----
public abstract record Stmt(SourceSpan Span) : Node(Span);
public sealed record Block(List<Stmt> Statements, SourceSpan Span) : Stmt(Span);
public sealed record ExprStmt(Expr Expr, SourceSpan Span) : Stmt(Span);
public sealed record IfStmt(Expr Condition, Stmt Then, Stmt? Else, SourceSpan Span) : Stmt(Span);
public sealed record WhileStmt(Expr Condition, Stmt Body, SourceSpan Span) : Stmt(Span);
public sealed record DoWhileStmt(Stmt Body, Expr Condition, SourceSpan Span) : Stmt(Span);
public sealed record ForStmt(Expr? Init, Expr? Condition, Expr? Step, Stmt Body, SourceSpan Span) : Stmt(Span);
/// <param name="Value">null for <c>default</c>.</param>
public sealed record SwitchCase(Expr? Value, Block Body, SourceSpan Span);
public sealed record SwitchStmt(Expr Value, List<SwitchCase> Cases, SourceSpan Span) : Stmt(Span);
public sealed record BreakStmt(SourceSpan Span) : Stmt(Span);
public sealed record ContinueStmt(SourceSpan Span) : Stmt(Span);
public sealed record ReturnStmt(Expr? Value, SourceSpan Span) : Stmt(Span);
public sealed record WaitStmt(Expr Duration, SourceSpan Span) : Stmt(Span);
public sealed record CallComboStmt(string Combo, SourceSpan Span) : Stmt(Span);
public sealed record EmptyStmt(SourceSpan Span) : Stmt(Span);

// ---- expressions ----
public abstract record Expr(SourceSpan Span) : Node(Span);
public sealed record IntLit(int Value, SourceSpan Span) : Expr(Span);
public sealed record StringLit(string Value, SourceSpan Span) : Expr(Span);
public sealed record NameExpr(string Name, SourceSpan Span) : Expr(Span);
public sealed record IndexExpr(Expr Target, Expr Index, SourceSpan Span) : Expr(Span);
public sealed record CallExpr(string Callee, List<Expr> Args, SourceSpan Span) : Expr(Span);
public sealed record UnaryExpr(TokenKind Op, Expr Operand, SourceSpan Span) : Expr(Span);
public sealed record BinaryExpr(TokenKind Op, Expr Left, Expr Right, SourceSpan Span) : Expr(Span);
public sealed record TernaryExpr(Expr Condition, Expr WhenTrue, Expr WhenFalse, SourceSpan Span) : Expr(Span);
/// <param name="Op">Assign, or a compound-assignment token.</param>
public sealed record AssignExpr(TokenKind Op, Expr Target, Expr Value, SourceSpan Span) : Expr(Span);
public sealed record IncDecExpr(bool Prefix, bool Increment, Expr Target, SourceSpan Span) : Expr(Span);
/// <summary><c>sizeof(int8)</c> sets TypeName; <c>sizeof(x)</c> sets Operand.</summary>
public sealed record SizeofExpr(string? TypeName, Expr? Operand, SourceSpan Span) : Expr(Span);
public sealed record AddrExpr(string Name, SourceSpan Span) : Expr(Span);
