using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ParserResult = FParsec.CharParsers.ParserResult<Microsoft.FSharp.Collections.FSharpList<Orion.Lang.Syntax.Pos<Orion.Lang.Syntax.Statement>>, Microsoft.FSharp.Core.Unit>;
using FunctionResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.FileBlock, Microsoft.FSharp.Core.Unit>;
using Statement = Orion.Ast.Statement;
using System.Security.Cryptography;
using Orion.Ast;
using Orion.Symbols;
using Orion.IR;
using Orion.Frontend;

namespace Orion.BuildTime
{
	public static class BuildTime
	{
		internal record CallContext(SourceFunctionSymbol Function, LinkedListNode<Tac> Callsite, Result Result);

		//Inputs
		internal static CallContext Context { private get; set; }

		//Outputs
		internal static string Output { get; set; } = string.Empty;
		internal static bool AssertFailed { get; set; } = false;

		public static File File_Open(string filename)
		{
			string[] lines = System.IO.File.ReadAllLines(filename);
			return new File
			{
				Lines = lines,
				Index = 0
			};
		}

		public static string File_ReadLine(File file)
		{
			string line = file.Lines[file.Index];
			file.Index++;
			return line;
		}

		public static bool File_HasLine(File file)
		{
			return file.Index < file.Lines.Length;
		}

		public static void Build_AddBody(string body)
		{
			//Front end parse.
			string trimmed = body.Trim();
			ParserResult result = Lang.Library.ParseStatements(trimmed);
			if (result.IsFailure)
			{
				ParserResult.Failure failure = result as ParserResult.Failure;
				Console.WriteLine("Parser error");
				Console.WriteLine(failure.Item1);
				Console.WriteLine();

				Console.WriteLine("Parser state");
				Console.WriteLine(failure.Item3);
				Console.WriteLine();
				if (Debugger.IsAttached)
					Debugger.Break();
				Environment.Exit(-1);
			}
			ParserResult.Success success = result as ParserResult.Success;
			List<Statement> statements = success.Item1.Select(i => Statement.Create(i.Value)).ToList();
			InputFile file = new InputFile(trimmed);

			//Type and symbol analysis, top down pass
			Binding.BindAst(Context.Function, statements, Context.Result);
			if (!Context.Result.Success)
				return;

			Codegen.Run(Context.Function, statements, Context.Result);
			if (!Context.Result.Success)
				return;

			List<Tac> statementTacs = statements.SelectMany(i => i.Tacs).ToList();
			foreach (Tac newTac in statementTacs)
			{
				if (newTac is DataTac || newTac is NopTac)
					continue;

				Context.Function.Tacs.AddBefore(Context.Callsite, newTac);
			}
		}

		public static Func Build_Func(string name, string returnType, string @params, string body)
		{
			string content =
@$"{returnType} {name}({@params})
{{
	{body}
}}";

			InputFile file = new InputFile(content);
			FunctionResult result = Lang.Library.ParseFunction(content);
			if (result.IsFailure)
			{
				FunctionResult.Failure failure = result as FunctionResult.Failure;
				Console.WriteLine("Parser error");
				Console.WriteLine(failure.Item1);
				Console.WriteLine();

				Console.WriteLine("Parser state");
				Console.WriteLine(failure.Item3);
				Console.WriteLine();
				if (Debugger.IsAttached)
					Debugger.Break();
				Environment.Exit(-1);
			}

			FunctionResult.Success success = result as FunctionResult.Success;
			Function function = FileBlock.Create(success.Item1 as Lang.Syntax.FileBlock.Function) as Function;

			//Create symbols for functions and structs in root symbol table
			Result createResult = new Result();
			Binding.BindAst(function, Context.Function.Table.GetRoot(), createResult);

			//Convert to IR
			{
				Result optResult = new Result();
				Codegen.Run(function, optResult);
				//Program.EnsureSuccess(optResult, file);
			}

			return new Func(function.Symbol);
		}

		public static void Invoke(Func f)
		{
			FunctionSymbol target = f.Function as FunctionSymbol;
			Trace.Assert(target.ReturnType == Context.Function.Table.Get<TypeSymbol>("void"));
			Trace.Assert(target.Parameters.Count == 0);

			CallTac tac = new CallTac(null, target, new List<DataSymbol>());
			Context.Function.Tacs.AddBefore(Context.Callsite, tac);
		}

		/*
		public static void InvokeArgs(Func f, string args)
		{
			//Get current function
			int current = _context.Peek();
			Context context = CallContexts[current];

			FunctionSymbol target = f.Function as FunctionSymbol;
			Trace.Assert(target.ReturnType == context.Function.Table.GetSymbol<TypeSymbol>("void"));
			Trace.Assert(target.Parameters.Count != 0);

			ArgsResult parse = Orion.Lang.Library.ParseArgs(args);
			Trace.Assert(parse.IsSuccess);
			ArgsResult.Success success = parse as ArgsResult.Success;
			List<Expression> expressions = success.Item1.Value.Select(i => Expression.Create(i.Item.Value)).ToList();

			Result result = Types.CheckTypes(context.Function, expressions);
			Trace.Assert(result.Success);

			//var statementTacs = expressions.SelectMany(i => TacCodegen.Codegen(context.Function, i));
			IEnumerable<Tac> tacs = TacCodegen.Codegen(context.Function, expressions);
			foreach (Tac newTac in tacs)
			{
				context.Function.Tacs.AddBefore(context.Callsite, newTac);
			}

			CallTac tac = new CallTac(CallTacOp.Runtime, null, f.Function as FunctionSymbol, new List<DataSymbol>());
			context.Function.Tacs.AddBefore(context.Callsite, tac);
		}
		*/
		public static void Assert(bool condition)
		{
			if (!condition)
				//throw new AssertFailedException();
				AssertFailed = true;
		}

		public static string Time_Now()
		{
			return DateTime.Now.ToString();
		}

		public static string i8_str(sbyte b)
		{
			return b.ToString();
		}

		public static string i16_str(short b)
		{
			return b.ToString();
		}

		public static string i32_str(int b)
		{
			return b.ToString();
		}
		public static string i64_str(long b)
		{
			return b.ToString();
		}

		public static string u8_str(byte b)
		{
			return b.ToString();
		}

		public static string u16_str(ushort b)
		{
			return b.ToString();
		}

		public static string u32_str(uint b)
		{
			return b.ToString();
		}
		public static string u64_str(ulong b)
		{
			return b.ToString();
		}

		public static string bool_str(bool b)
		{
			return b.ToString();
		}

		public static int str_len(string s)
		{
			return s.Length;
		}

		public static string StrConcat(string s1, string s2)
		{
			return s1 + s2;
		}

		public static void WriteLine(string s)
		{
			Output += s + Environment.NewLine;
		}

		public static string Func_Name(Func f)
		{
			return f.Function.Name;
		}

		public static byte[] str_md5(string s)
		{
			byte[] inputBytes = Encoding.ASCII.GetBytes(s);
			return MD5.HashData(inputBytes);
		}

		public static string bytes_hexstr(byte[] input)
		{
			return Convert.ToHexString(input);
		}

		public static void WriteInts(int[] ints)
		{
			WriteLine(string.Join(",", ints));
		}

		public static Solver Solver_Make(Func[] funcs)
		{
			List<SourceFunctionSymbol> underlying = funcs.Select(i => i.Function).ToList();
			return new Solver(new Orion.Solver.SolverEngine(underlying, Context.Function.Table.GetRoot()));
		}

		public static void Solver_Solve(Solver solver)
		{
			solver.Engine.Solve();
		}

		public static string Solver_Struct(Solver solver)
		{
			return solver.Engine.DeclareStruct();
		}

		public static string Solver_Main(Solver solver)
		{
			return solver.Engine.GenerateMain();
		}

		public static string Solver_ViewState(Solver solver)
		{
			return solver.Engine.ViewState();
		}
	}
}
