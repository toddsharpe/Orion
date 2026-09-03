using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Orion.Ast;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Linq;

namespace Orion.LangSvr
{
	//Hover: the most specific bound expression under the cursor, and its type.
	public static class OrionHover
	{
		public static Hover At(Analysis analysis, int line0Based, int char0Based)
		{
			if (analysis?.Ast == null)
				return null;

			long line = line0Based + 1; // InputRegion is 1-based (FParsec)
			long col = char0Based + 1;

			//1) The deepest bound leaf under the cursor; declaration nodes resolve by identifier text below.
			Node best = null;
			long bestSize = long.MaxValue;
			void Consider(Node n, InputRegion r)
			{
				if (r == null || !Contains(r, line, col))
					return;
				long size = (r.Stop.Line - r.Start.Line) * 1_000_000L + (r.Stop.Column - r.Start.Column);
				if (size < bestSize)
				{
					bestSize = size;
					best = n;
				}
			}

			foreach (Node n in analysis.Ast.DescendantsAndSelf())
			{
				switch (n)
				{
					case Call call when call.Callee != null:  // a call reference (considered even when void)
						Consider(call, call.Region);
						break;
					case Expression e when e.Symbol != null && e.Symbol.Type != null:
						Consider(e, e.Region);
						break;
				}
			}

			if (best != null)
			{
				string leaf = best is Call call && call.Callee != null
					? (call.Callee is BuiltinFunctionSymbol ? "builtin " : "") + Signature(call.Callee)
					: Describe((Expression)best);
				return Markup(leaf, ToRange(best.Region));
			}

			//2) No bound leaf: resolve against the enclosing function's declarations -- a #param solver block never binds, so its ports and locals are only describable from syntax -- and before file scope, so a shadowing local wins.
			string ident = OrionScope.IdentifierAt(analysis.Text, line0Based, char0Based);
			if (ident != null)
			{
				string declared = Declaration(OrionScope.Enclosing(analysis, line, col), ident, analysis.Text);
				if (declared != null)
					return Markup(declared, null);

				foreach (Node n in analysis.Ast.DescendantsAndSelf())
				{
					if (n is Enum en && en.Name == ident) return Markup(EnumBody(en), null);
					if (n is Struct st && st.Name == ident) return Markup(StructBody(st), null);
					if (n is Function fn && fn.Name == ident) return Markup(Signature(fn), null);
				}
				// #param solver-block templates were removed from the tu; match their names here too.
				if (analysis.Templates != null)
					foreach (Function t in analysis.Templates)
						if (t.Name == ident) return Markup(Signature(t), null);

				//An identifier we can't resolve shows nothing, rather than the enclosing function.
				return null;
			}

			//3) Not on an identifier: the smallest enclosing scope whose region contains the cursor.
			Node scope = null;
			long scopeSize = long.MaxValue;
			void ConsiderScope(Node n)
			{
				InputRegion r = ScopeRegion(n);
				if (r == null || !Contains(r, line, col))
					return;
				long size = (r.Stop.Line - r.Start.Line) * 1_000_000L + (r.Stop.Column - r.Start.Column);
				if (size < scopeSize)
				{
					scopeSize = size;
					scope = n;
				}
			}
			foreach (Node n in analysis.Ast.DescendantsAndSelf())
				ConsiderScope(n);
			if (analysis.Templates != null)
				foreach (Function t in analysis.Templates)
				{
					ConsiderScope(t);
					foreach (Node n in t.DescendantsAndSelf())
						ConsiderScope(n);
				}

			string scopeText = scope == null ? null : ScopeLabel(scope, analysis.Text);
			return scopeText == null ? null : Markup(scopeText, ToRange(scope.Region));
		}

		// The scopes a hover can fall back to (declarations + control-flow/blocks); null = not a scope.
		private static InputRegion ScopeRegion(Node n) => n switch
		{
			Function or Struct or Enum or If or IfElse or For or While or Scope or RunExpr => n.Region,
			_ => null
		};

		// A one-line label for a scope; for control flow we slice its header text out of the source.
		private static string ScopeLabel(Node n, string text) => n switch
		{
			Function fn => Signature(fn),
			Struct st => StructBody(st),
			Enum en => EnumBody(en),
			If i => "if (" + Slice(text, i.Clause?.Region) + ")",
			IfElse i => "if (" + Slice(text, i.Clause?.Region) + ")",
			While w => "while (" + Slice(text, w.Condition?.Region) + ")",
			For => "for (...)",
			// A build-time block is a RunExpr now; a Scope is a plain nested `{ }`.
			RunExpr => "#run { ... }",
			Scope => "{ ... }",
			_ => null
		};

		// The source substring covered by a region (1-based, Stop column exclusive); trimmed, "..." if multi-line.
		private static string Slice(string text, InputRegion r)
		{
			if (text == null || r == null)
				return "";
			string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			int sl = (int)r.Start.Line - 1, el = (int)r.Stop.Line - 1;
			if (sl < 0 || sl >= lines.Length)
				return "";
			int sc = System.Math.Max(0, (int)r.Start.Column - 1);
			if (sl == el)
			{
				int ec = System.Math.Min(lines[sl].Length, (int)r.Stop.Column);
				return ec > sc ? lines[sl].Substring(sc, ec - sc).Trim() : "";
			}
			return lines[sl].Substring(System.Math.Min(sc, lines[sl].Length)).Trim() + " ...";
		}

		private static Hover Markup(string text, LspRange range) =>
			new Hover
			{
				Contents = new MarkedStringsOrMarkupContent(new MarkupContent
				{
					Kind = MarkupKind.Markdown,
					Value = "```orion\n" + text + "\n```"
				}),
				Range = range
			};

		// `ident` as declared inside `fn` -- a parameter as written, a #state/local/const in Describe's "kind name: type" shape -- or null when `fn` declares no such name.
		private static string Declaration(Function fn, string ident, string text)
		{
			if (fn == null)
				return null;

			//Parameters first, since a port and a body local share a name only in invalid code; a port shows as written, because its directive and `@ net` are the whole point of it.
			foreach (Parameter p in fn.Parameters)
				if (p.Name == ident)
				{
					string written = ParamText(text, p.Region);
					return written.Length > 0 ? written : Label(p) + " " + p.Name + ": " + TypeText(p.TypeName);
				}

			foreach (Node n in fn.DescendantsAndSelf())
			{
				if (n is Construct c && c.SymbolName == ident)
					return Label(c.Directive, false) + " " + c.SymbolName + ": " + TypeText(c.TypeName);
				if (n is ConstDef cd && cd.Name == ident)
					return Label(cd.Directive, true) + " " + cd.Name + ": " + TypeText(cd.TypeName);
			}

			return null;
		}

		// Fallback when a parameter has no usable region: a port keeps its directive, a plain parameter reads as "parameter".
		private static string Label(Parameter p) =>
			p.Directive == ParamDirective.None ? "parameter" : AstDir(p.Directive).Trim();

		private static string Label(LocalDirective d, bool isConst) =>
			d == LocalDirective.State ? "state" : isConst ? "const" : "local";

		// One parameter's declaration as written: its region runs to the following separator, so a trailing `,` or `)` is trimmed and a region reaching onto the next line is cut at the end of the first.
		private static string ParamText(string text, InputRegion r)
		{
			if (text == null || r == null)
				return "";
			string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			int sl = (int)r.Start.Line - 1;
			if (sl < 0 || sl >= lines.Length)
				return "";
			int sc = System.Math.Max(0, (int)r.Start.Column - 1);
			if (sc >= lines[sl].Length)
				return "";
			int ec = r.Stop.Line == r.Start.Line
				? System.Math.Min(lines[sl].Length, (int)r.Stop.Column)
				: lines[sl].Length;
			return ec > sc ? Collapse(TrimSeparators(lines[sl].Substring(sc, ec - sc))) : "";
		}

		// A `)` is only a separator when unbalanced in the slice, so a net expression's own parentheses survive.
		private static string TrimSeparators(string s)
		{
			s = s.TrimEnd();
			while (s.EndsWith(",") || (s.EndsWith(")") && s.Count(c => c == ')') > s.Count(c => c == '(')))
				s = s.Substring(0, s.Length - 1).TrimEnd();
			return s;
		}

		//Source alignment (`#input  f64 x`) is padding in the file but noise in a one-line hover.
		private static string Collapse(string s) =>
			string.Join(" ", s.Split(' ', System.StringSplitOptions.RemoveEmptyEntries));

		//TypeName.Name already carries any generic arguments (`List<Device>`), and it is all an unbound declaration has.
		private static string TypeText(TypeName t) => t?.Name ?? "?";

		// Signature from a function declaration's AST (shows #param/#input/#output directives on solver blocks).
		private static string Signature(Function fn)
		{
			string ps = string.Join(", ", fn.Parameters.Select(p => AstDir(p.Directive) + p.TypeName.Name + " " + p.Name));
			return fn.ReturnType.Name + " " + fn.Name + "(" + ps + ")";
		}

		// Signature from a resolved callee symbol (a function reference / call).
		private static string Signature(FunctionSymbol f)
		{
			string ps = string.Join(", ", f.Parameters.Select(p => SymDir(p) + p.Type.Name + " " + p.Name));
			return f.ReturnType.Name + " " + f.Name + "(" + ps + ")";
		}

		private static string AstDir(ParamDirective d)
		{
			switch (d)
			{
				case ParamDirective.Input: return "#input ";
				case ParamDirective.Prev: return "#prev ";
				case ParamDirective.Output: return "#output ";
				case ParamDirective.Param: return "#param ";
				default: return "";
			}
		}

		//The symbol, not just the direction: a `#prev` port IS an In, and saying `#input` would be a lie.
		private static string SymDir(ParamDataSymbol p)
		{
			switch (p.Direction)
			{
				case ParamDirection.In: return p.Delayed ? "#prev " : "#input ";
				case ParamDirection.Out: return "#output ";
				case ParamDirection.State: return "#state ";
				default: return "";
			}
		}

		private static bool Contains(InputRegion r, long line, long col)
		{
			bool afterStart = line > r.Start.Line || (line == r.Start.Line && col >= r.Start.Column);
			bool beforeStop = line < r.Stop.Line || (line == r.Stop.Line && col <= r.Stop.Column);
			return afterStart && beforeStop;
		}

		private static string Describe(Expression e)
		{
			TypeSymbol type = e.Symbol.Type;

			// An enum literal (e.g. Phase::Coast) -> the specific member and its numeric value.
			if (e.Symbol is LiteralSymbol lit && type is EnumTypeSymbol litEnum && lit.Value != null)
			{
				string member = lit.Value.ToString();
				Member found = litEnum.Members.FirstOrDefault(m => m.Name == member);
				return type.Name + "::" + member + " = " + (found != null ? found.Value : System.Convert.ToInt32(lit.Value));
			}

			//An enum- or struct-typed symbol appends the whole type's members under the description.
			string typeBody =
				type is EnumTypeSymbol en ? "\n" + EnumBody(en) :
				type is StructTypeSymbol stt ? "\n" + StructBody(stt) :
				"";
			//Calls are rendered by At via Signature(Callee); the remaining leaf is a Variable.
			switch (e)
			{
				case Variable v:
					string kind = Kind(v.Symbol);
					return (kind.Length > 0 ? kind + " " : "") + v.SymbolName + ": " + type.Name + typeBody;
				default:
					return typeBody.Length > 0 ? typeBody.Substring(1) : type.Name;
			}
		}

		// Struct body from its declaration AST (field types come straight from the parsed TypeNames).
		private static string StructBody(Struct s)
		{
			string fields = string.Join(" ", s.Fields.Select(f => f.TypeName.Name + " " + f.Name + ";"));
			return "struct " + s.Name + " { " + fields + " }";
		}

		// Struct body from a bound type symbol (for a struct-typed variable/param/construction).
		private static string StructBody(StructTypeSymbol s)
		{
			string fields = string.Join(" ", s.Fields.Select(f => f.Type.Name + " " + f.Name + ";"));
			return "struct " + s.Name + " { " + fields + " }";
		}

		private static string EnumBody(EnumTypeSymbol e)
		{
			string members = string.Join(", ", e.Members.Select(m => m.Name + " = " + m.Value));
			return "enum " + e.Name + " { " + members + " }";
		}

		//The same rendering from the AST enum declaration, for identifier-text lookup.
		private static string EnumBody(Enum e)
		{
			string members = string.Join(", ", e.Members.Select(m => m.Name + " = " + m.Value));
			return "enum " + e.Name + " { " + members + " }";
		}

		private static string Kind(DataSymbol sym)
		{
			switch (sym)
			{
				case ParamDataSymbol _: return "parameter";
				case LocalDataSymbol l: return l.IsReadOnly ? "const" : "local";
				default: return "";
			}
		}

		private static LspRange ToRange(InputRegion r)
		{
			int sl = Max0((int)r.Start.Line - 1);
			int sc = Max0((int)r.Start.Column - 1);
			int el = Max0((int)r.Stop.Line - 1);
			int ec = Max0((int)r.Stop.Column);
			return new LspRange(new LspPosition(sl, sc), new LspPosition(el, ec));
		}

		private static int Max0(int x) => x < 0 ? 0 : x;
	}
}
