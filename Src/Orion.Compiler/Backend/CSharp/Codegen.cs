using Orion.Backend.Render;
using Orion.BuildTime;
using Orion.Graphs;
using Orion.IR;
using Orion.Backend.StIr;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend.CSharp
{
	//Renders the program as C#. The CLR has the language's own integer widths and wraps in hardware, so none of the masking the script backends carry appears here.
	internal partial class Codegen : ModuleBackend
	{
		//The namespace the output declares, named for the output file; null falls back to `Program` in the Writer.
		private readonly string _name;

		internal Codegen(string name = null)
		{
			_name = NamespaceName(name);
		}

		//A basename bent to a legal namespace: illegal characters become '_', a digit-led one is prefixed, a keyword escapes as any identifier does.
		private static string NamespaceName(string name)
		{
			if (string.IsNullOrEmpty(name))
				return null;

			StringBuilder sb = new StringBuilder(name.Length);
			foreach (char c in name)
				sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

			return Ident(char.IsDigit(sb[0]) ? $"_{sb}" : sb.ToString());
		}

		public override string Render(SymbolTable root, CallGraph.Node main)
		{
			File generated = Generate(root, main);

			//The CLR entry point is a method, not a top-level statement, so it is one more function in the same class. A library has none: its `main` was `#build` and ran during the build.
			if (main?.Value is SourceFunctionSymbol entry)
				generated.Functions.Add(Entry(entry));

			Writer writer = new Writer(_name);
			writer.Write(generated);
			return writer.ToString();
		}

		//The output names the runtime bare (WriteLine, OrionArray, i32_str): Runtimes/CSharp compiles alongside, reached by these `using static` lines.
		protected override List<Reference> Includes =>
		[
			new Reference("System"),
			new Reference("static Orion"),
			new Reference("static Orion_platform"),
		];

		protected override string TypeName(TypeSymbol type) => Cs(type);

		protected override string EnumName(string member) => Ident(member);

		protected override string Identifier(string name) => Ident(name);

		protected override string Value(DataSymbol symbol) => Cs(symbol);

		//A global with no initializer is zeroed (C++'s `= {}`): an exported solver reads a struct global before it writes it on the first cycle, and a null field would throw there.
		protected override string Zero(TypeSymbol type) => ZeroValue(type);

		protected override Declaration Rtti(SourceFunctionSymbol function) =>
			new Declaration("OrionFunction", $"{Ident(function.Name)}Function", $"new OrionFunction(\"{function.Name}\")");

		private static Function Entry(SourceFunctionSymbol entry)
		{
			List<Code> code = Language.IsVoid(entry.ReturnType)
				? [new Line($"{Ident(entry.Name)}();"), new Line("return 0;")]
				: [new Line($"return {Ident(entry.Name)}();")];

			return new Function("int", "Main", [], new Dictionary<string, List<Declaration>>(), code);
		}

		protected override List<Function> CreateFunctions(SymbolTable root, List<SourceFunctionSymbol> reachable)
		{
			return reachable.Select(i =>
			{
				List<Code> body = Statements.Run(i.St);

				//A non-void C# function cannot run off its end, and the relooper may leave a body ending in a loop it proves nothing about; the trailing return costs a disabled warning where unneeded.
				if (!Language.IsVoid(i.ReturnType) && !EndsWithReturn(body))
					body.Add(new Line("return default;"));

				//Every local and temp is declared AND initialized: C# rejects a read of an unassigned local, and a generated body assigns in the relooper's order rather than the source's.
				Dictionary<string, List<Declaration>> locals = Frame(i, Binding, Declare, body);

				//A wired block takes the state alone; the struct emits as a class, so the reference is the by-ref.
				List<string> args = i.Wired ? [$"{Solver.StructName} {Solver.ParamName}"] : i.Parameters.Select(Declare).ToList();
				return new Function(Cs(i.ReturnType), Ident(i.Name), args, locals, body);
			}).ToList();
		}

		//Whether control cannot reach the end of a rendered body. Only a literal trailing `return` counts: anything subtler is what the extra `return default;` is for.
		private static bool EndsWithReturn(List<Code> body)
		{
			return body.Count > 0 && body[^1] switch
			{
				Line l => l.Text.StartsWith("return"),
				CodeBlock c => c.Lines.LastOrDefault(i => !string.IsNullOrEmpty(i))?.StartsWith("return") == true,
				_ => false,
			};
		}

		//An `#output` or `#state` parameter is written through, which is what C# `ref` means. Not `out`: Orion's is an in-out, and `out` would forbid the read and demand definite assignment.
		private static string Declare(ParamDataSymbol symbol) =>
			$"{(symbol.Direction.IsWritable() ? "ref " : string.Empty)}{Cs(symbol.Type)} {Ident(symbol.Name)}";

		//A wired port's entry binding: an input copies (a class-typed one aliases, as its parameter did), while #state and #output write through a ref local.
		private static Declaration Binding(ParamDataSymbol port)
		{
			string cs = Cs(port.Type);
			string cell = Netlist.Cell(port);
			return port.Direction == ParamDirection.In
				? new Declaration(cs, Ident(port.Name), cell)
				: new Declaration($"ref {cs}", Ident(port.Name), $"ref {cell}");
		}

		//C#'s tokens for the shared StCtrl walk in Backend/Render/StmtPrinter.
		private sealed class Printer : StmtPrinter
		{
			protected override string Forever => "true";
			protected override string End => ";";
			protected override string Not(StExpr condition) => $"!{PrintExpr(condition, ExprPrinter.UnaryPrec)}";
			protected override string Expr(StExpr e) => PrintExpr(e);
			protected override string Name(DataSymbol symbol) => Cs(symbol);
			protected override IEnumerable<string> Raw(Tac tac) => Codegen.Raw(tac);
		}

		private static readonly Printer Statements = new Printer();

		//A local or temp declaration, always initialized: C# forbids reading an unassigned local, and the relooper's order is not the source's.
		private static Declaration Declare(NamedDataSymbol sym)
		{
			string type = Cs(sym.Type);
			string init = sym.Type switch
			{
				//A 2-D temp's element stores index into its rows, so the rows must exist before them.
				ArrayTypeSymbol { Element: BufferTypeSymbol } when sym is TempDataSymbol => ZeroValue(sym.Type),
				//An array temp materializes a non-constant literal element by element, so it needs a buffer; a local is always pointed at an existing one first, so an empty view is enough.
				BufferTypeSymbol b when sym is TempDataSymbol => $"new {type}(new {Cs(b.Element)}[{sym.Dimension}], {sym.Dimension})",
				BufferTypeSymbol b => $"new {type}(new {Cs(b.Element)}[0], 0)",
				_ => ZeroValue(sym.Type),
			};

			return new Declaration(type, Ident(sym.Name), init);
		}
	}
}
