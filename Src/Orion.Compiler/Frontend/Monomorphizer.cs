using Orion.Ast;
using Orion.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;

namespace Orion.Frontend
{
	//One unit's generic templates and what they instantiated; kept on the session past Expand, since a `#insert` produces code long after and may still need an instantiation.
	internal sealed class Generics
	{
		internal Dictionary<string, Function> Templates = new Dictionary<string, Function>();
		internal HashSet<string> Instantiated = new HashSet<string>();
		internal Dictionary<string, Struct> StructTemplates = new Dictionary<string, Struct>();
		internal HashSet<string> StructInstantiated = new HashSet<string>();

		//Constants a size argument may name, so `Buf<Window>` is `Buf_8`.
		internal Dictionary<string, int> SizeConsts = new Dictionary<string, int>();

		//Struct instantiations named but not yet cloned; every entry point drains it before returning.
		internal readonly Queue<(Struct Template, Dictionary<string, TypeName> Map, string Name)> StructWork = new Queue<(Struct, Dictionary<string, TypeName>, string)>();
	}

	//C++-style generics: each <T> instantiation is cloned into a concrete function before binding.
	public static class Monomorphizer
	{
		//The private helpers read the session's generics here rather than take it through ten signatures; every public entry is handed the ambient session.
		private static Generics Generics => Compiler.Session.Generics;

		//The no-substitution map the walks over concrete code share.
		private static readonly Dictionary<string, TypeName> NoMap = new Dictionary<string, TypeName>();

		public static void Expand(TranslationUnit tu, CompileSession session, List<Message> messages)
		{
			//Conditionals kept the compile's facts on the session; a #src unit never runs it, so its own are built here.
			session.TypeFacts ??= TypeFacts.From(tu);

			//Collect templates and remove them from the unit (they have open types, cannot bind).
			session.Generics = new Generics
			{
				Templates = tu.Blocks
					.OfType<Function>()
					.Where(i => i.TypeParameters.Count > 0)
					.ToDictionary(i => i.Name),
				StructTemplates = tu.Blocks
					.OfType<Struct>()
					.Where(i => i.TypeParameters.Count > 0)
					.ToDictionary(i => i.Name),
				SizeConsts = tu.Blocks.OfType<Const>()
					.Where(i => i.Value is IntLiteral or TypedIntLiteral)
					.Select(i => (i.Name, Value: Convert.ToInt64(i.Value.Boxed)))
					.Where(i => i.Value >= 0 && i.Value <= int.MaxValue)
					.GroupBy(i => i.Name)
					.ToDictionary(i => i.Key, i => (int)i.First().Value),
			};
			if (session.Generics.Templates.Count == 0 && session.Generics.StructTemplates.Count == 0)
				return;

			tu.Blocks = tu.Blocks.Where(i =>
				(i is not Function f || f.TypeParameters.Count == 0) &&
				(i is not Struct s || s.TypeParameters.Count == 0)).ToList();

			//The concrete blocks that carry types but no statements: struct fields, constants, extern signatures.
			foreach (Struct s in tu.Blocks.OfType<Struct>())
				s.Fields = [.. s.Fields.Select(i => i with { TypeName = Rewrite(i.TypeName, NoMap, messages) })];

			foreach (Const c in tu.Blocks.OfType<Const>())
				RewriteConst(c, messages);

			foreach (Extern e in tu.Blocks.OfType<Extern>())
			{
				e.ReturnType = Rewrite(e.ReturnType, NoMap, messages);
				foreach (Parameter p in e.Parameters)
					p.TypeName = Rewrite(p.TypeName, NoMap, messages);
			}

			Queue<(Function Fn, Dictionary<string, TypeName> Map)> work = new Queue<(Function, Dictionary<string, TypeName>)>();

			//Seed with the ordinary functions; they carry no substitution.
			foreach (Function fn in tu.Blocks.OfType<Function>())
				work.Enqueue((fn, new Dictionary<string, TypeName>()));

			Drain(work, tu.Blocks.Add, session, messages);
			DrainStructs(tu.Blocks.Add, messages);
		}

		//A constant's type and value carry TypeNames the statement walk never sees.
		private static void RewriteConst(Const c, List<Message> messages)
		{
			c.TypeName = Rewrite(c.TypeName, NoMap, messages);
			RewriteLiteral(c.Value, messages);
			if (c.Initializer != null)
				c.Initializer = (Expression)c.Initializer.Rewrite(node => Substitute(node, null, NoMap, messages));
		}

		private static void RewriteLiteral(Literal value, List<Message> messages)
		{
			if (value == null)
				return;

			value.TypeName = Rewrite(value.TypeName, NoMap, messages);
			switch (value)
			{
				case StructVal { Value: Dictionary<string, Literal> fields }:
					foreach (Literal field in fields.Values)
						RewriteLiteral(field, messages);
					break;

				case ArrayVal { Value: Literal[] elements }:
					foreach (Literal element in elements)
						RewriteLiteral(element, messages);
					break;
			}
		}

		//Expand a unit compiled apart mid-build (`#src`): its templates are its own, and the outer compile's survive the load.
		public static void ExpandIsolated(TranslationUnit tu, CompileSession session, List<Message> messages)
		{
			Generics outer = session.Generics;
			TypeFacts facts = session.TypeFacts;
			try
			{
				//Cleared so Expand builds this unit's own facts rather than folding against the outer compile's.
				session.TypeFacts = null;
				Expand(tu, session, messages);
			}
			finally
			{
				session.Generics = outer;
				session.TypeFacts = facts;
			}
		}

		//Expand the generic calls in code the BUILD produced -- a `#insert` body, a `#param` clone -- and hand back the new instantiations; the caller binds them, since they did not exist when Binding ran.
		public static List<Function> ExpandLate(List<Statement> body, CompileSession session, List<Message> messages)
		{
			List<Function> created = new List<Function>();
			if (session.Generics.Templates.Count == 0 && session.Generics.StructTemplates.Count == 0)
				return created;

			Queue<(Function Fn, Dictionary<string, TypeName> Map)> work = new Queue<(Function, Dictionary<string, TypeName>)>();

			//The body is not a function, so it is walked directly; anything it instantiates drains through the same loop, so a generic calling a generic works here too.
			WalkStatements(body, call => OnCall(call, work, created.Add, messages), new Dictionary<string, TypeName>(), messages);
			Drain(work, created.Add, session, messages);

			//A fragment may not be the first to name a struct instantiation: there is no unit here to add it to.
			while (session.Generics.StructWork.Count > 0)
			{
				(Struct _, Dictionary<string, TypeName> _, string name) = session.Generics.StructWork.Dequeue();
				messages.Add(new Message($"Spliced code is the first to name struct instantiation {name}; name it once in compiled source so the build can instantiate it.", InputRegion.None, MessageType.Error));
			}

			return created;
		}

		//Substitute each queued function's signature and body, instantiating what its calls name until nothing is left; `onCreated` receives each new instantiation.
		private static void Drain(
			Queue<(Function Fn, Dictionary<string, TypeName> Map)> work,
			Action<Function> onCreated,
			CompileSession session,
			List<Message> messages)
		{
			while (work.Count > 0)
			{
				(Function fn, Dictionary<string, TypeName> map) = work.Dequeue();

				//Substitute type parameters throughout the signature.
				fn.ReturnType = Rewrite(fn.ReturnType, map, messages);
				foreach (Parameter param in fn.Parameters)
					param.TypeName = Rewrite(param.TypeName, map, messages);

				//Before WalkStatements, so a generic call in a dead branch is never instantiated either.
				if (map.Count > 0)
					Conditionals.Fold(fn.Body, new FoldEnv { Values = Conditionals.Defines(session), Types = map, Facts = session.TypeFacts, UndefinedIsFalse = true }, messages);

				WalkStatements(fn.Body, call => OnCall(call, work, onCreated, messages), map, messages);
			}
		}

		//Substitute the parameters, then fold any struct-template reference into the instantiation it names.
		private static TypeName Rewrite(TypeName type, Dictionary<string, TypeName> map, List<Message> messages)
		{
			return StructRef(Subst(type, map), messages);
		}

		private static bool AllDigits(string name) => name.Length > 0 && name.All(char.IsDigit);

		//Canonicalize a size argument: a literal constant's name becomes its value, so `Buf<Window>` is `Buf_8`.
		private static TypeName Sized(TypeName arg)
		{
			if (arg == null || arg.IsGeneric || arg.IsArray || arg.Measure != null)
				return arg;

			return Generics.SizeConsts.TryGetValue(arg.Name, out int value)
				? new TypeName { Name = value.ToString(), Region = arg.Region }
				: arg;
		}

		//`Box<f32>` becomes the concrete `Box_f32`, instantiated at first sight; inner arguments fold first.
		private static TypeName StructRef(TypeName type, List<Message> messages)
		{
			if (type == null || !type.IsGeneric || Generics.StructTemplates.Count == 0)
				return type;

			List<TypeName> args = [.. type.Generics.Select(i => Sized(StructRef(i, messages)))];

			if (!Generics.StructTemplates.TryGetValue(type.GenericType, out Struct template))
				return args.SequenceEqual(type.Generics) ? type : TypeName.CreateGeneric(type.GenericType, args);

			if (args.Count != template.TypeParameters.Count)
			{
				messages?.Add(new Message($"Struct template {type.GenericType} expects {template.TypeParameters.Count} type argument(s), got {args.Count}.", type.Region, MessageType.Error));
				return type;
			}

			string mangled = Mangle(type.GenericType, args);
			if (Generics.StructInstantiated.Add(mangled))
			{
				Dictionary<string, TypeName> bound = template.TypeParameters
					.Zip(args)
					.ToDictionary(i => i.First, i => i.Second);
				Generics.StructWork.Enqueue((template, bound, mangled));
				messages?.Add(new Message($"Expanded struct {type.GenericType}<{string.Join(", ", args.Select(i => i.Name))}> as {mangled}.", type.Region, MessageType.Trace));
			}

			return new TypeName { Name = mangled, Region = type.Region };
		}

		//Each pending instantiation clones the template's fields under its substitution; a field may queue more.
		private static void DrainStructs(Action<Struct> onCreated, List<Message> messages)
		{
			while (Generics.StructWork.Count > 0)
			{
				(Struct template, Dictionary<string, TypeName> map, string name) = Generics.StructWork.Dequeue();
				onCreated(new Struct
				{
					Name = name,
					IsBuild = template.IsBuild,
					IsExport = template.IsExport,
					Fields = [.. template.Fields.Select(i => i with { TypeName = Rewrite(Clone(i.TypeName), map, messages) })],
					Region = template.Region,
				});
			}
		}

		//One generic call: make its instantiation if this is the first to ask for it, and rewrite the call to name it either way.
		private static void OnCall(
			Call call,
			Queue<(Function Fn, Dictionary<string, TypeName> Map)> work,
			Action<Function> onCreated,
			List<Message> messages)
		{
			//Only user templates; builtin generics and non-generic calls are untouched.
			if (call.GenericArgs.Count == 0 || !Generics.Templates.TryGetValue(call.Function, out Function template))
				return;

			call.GenericArgs = [.. call.GenericArgs.Select(Sized)];
			string mangled = Mangle(call.Function, call.GenericArgs);

			if (Generics.Instantiated.Add(mangled))
			{
				if (call.GenericArgs.Count != template.TypeParameters.Count)
				{
					messages.Add(new Message($"Generic function {call.Function} expects {template.TypeParameters.Count} type argument(s), got {call.GenericArgs.Count}.", call.Region, MessageType.Error));
				}
				else
				{
					//Fresh AST copy from the parse tree so binding state is independent per instantiation.
					Function clone = (Function)FileBlock.Create(template.Source);
					clone.Name = mangled;
					clone.TypeParameters = new List<string>();

					Desugar.Run(clone, messages);

					Dictionary<string, TypeName> childMap = template.TypeParameters
						.Zip(call.GenericArgs)
						.ToDictionary(i => i.First, i => i.Second);

					onCreated(clone);
					work.Enqueue((clone, childMap));

					messages.Add(new Message($"Expanded {call.Function}<{string.Join(", ", call.GenericArgs.Select(i => i.Name))}> as {mangled}.", call.Region, MessageType.Trace));
				}
			}

			//Rewrite the call to the concrete instantiation.
			call.Function = mangled;
			call.GenericArgs = new List<TypeName>();
		}

		//Cached because this runs over every node of every instantiation.
		private static readonly Dictionary<Type, PropertyInfo[]> _typeNameProperties = new Dictionary<Type, PropertyInfo[]>();

		private static PropertyInfo[] TypeNameProperties(Type type)
		{
			if (!_typeNameProperties.TryGetValue(type, out PropertyInfo[] properties))
			{
				//Writable only: Substitute assigns through these, and a `List<TypeName>` carries a call's generic arguments.
				properties = [.. type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					.Where(i => (i.PropertyType == typeof(TypeName) || i.PropertyType == typeof(List<TypeName>))
						&& i.GetIndexParameters().Length == 0 && i.CanRead && i.CanWrite)];
				_typeNameProperties[type] = properties;
			}

			return properties;
		}

		//Re-apply the unit pass's rewrites to a `#param` clone reparsed from source: its signature types and the calls its body mangles; naming only -- ExpandLate makes any missing instantiation later.
		public static void RewriteClone(Function clone, CompileSession session)
		{
			if (session.Generics.Templates.Count == 0 && session.Generics.StructTemplates.Count == 0)
				return;

			clone.ReturnType = Rewrite(clone.ReturnType, NoMap, null);
			foreach (Parameter param in clone.Parameters)
				param.TypeName = Rewrite(param.TypeName, NoMap, null);

			void MangleCall(Call call)
			{
				if (call.GenericArgs.Count > 0 && session.Generics.Templates.ContainsKey(call.Function))
				{
					call.Function = Mangle(call.Function, [.. call.GenericArgs.Select(Sized)]);
					call.GenericArgs = new List<TypeName>();
				}
			}

			WalkStatements(clone.Body, MangleCall, NoMap, null);

			//A clone cannot be the first to name a struct instantiation; forget it, so a later real use still can.
			while (session.Generics.StructWork.Count > 0)
				session.Generics.StructInstantiated.Remove(session.Generics.StructWork.Dequeue().Name);
		}

		//Mangle a template name + type arguments into a valid identifier: max<i32> -> max_i32.
		private static string Mangle(string name, List<TypeName> args)
		{
			return $"{name}_{string.Join("_", args.Select(i => Sanitize(i.Name)))}";
		}

		//A name as an identifier: anything but a letter or digit becomes `_`, as every mangling spells it.
		internal static string Sanitize(string name)
		{
			return new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
		}

		//Substitute a type-parameter suffix on a typed literal (0:T -> 0:i32), picking the literal kind to match.
		private static Literal SubstLiteral(Literal literal, Dictionary<string, TypeName> map)
		{
			string code = literal switch
			{
				TypedIntLiteral intLit => intLit.Code,
				TypedFloatLiteral floatLit => floatLit.Code,
				_ => null
			};
			if (code == null || !map.TryGetValue(code, out TypeName concrete))
				return literal;

			//A measured T picks its kind by the carrying primitive; the literal itself keeps the full spelling.
			string name = concrete.MeasureBase ?? concrete.Name;
			if (name == "f32" || name == "f64")
			{
				double value = Convert.ToDouble(literal.Boxed);
				return new TypedFloatLiteral { Value = value, Code = concrete.Name, TypeName = Clone(concrete), Region = literal.Region };
			}

			long ivalue = Convert.ToInt64(literal.Boxed);
			return new TypedIntLiteral { Value = ivalue, Code = concrete.Name, TypeName = Clone(concrete), Region = literal.Region };
		}

		//Substitute type parameters within a type name (recurses through generics/arrays).
		private static TypeName Subst(TypeName type, Dictionary<string, TypeName> map)
		{
			if (type == null || map.Count == 0)
				return type;

			//Direct type parameter: T -> concrete.
			if (!type.IsArray && !type.IsGeneric && map.TryGetValue(type.Name, out TypeName direct))
				return Clone(direct);

			//Bracket form over a type parameter: T[4] -> concrete[4]. A view is a generic name, handled below.
			if (type.IsArray && map.TryGetValue(type.ElementType, out TypeName element))
				return SubstExtents(ArrayOf(type, element), map);

			//Generic reference containing type parameters: List<T> -> List<concrete>.
			if (type.IsGeneric)
				return TypeName.CreateGeneric(type.GenericType, type.Generics.Select(i => Subst(i, map)).ToList());

			return SubstExtents(type, map);
		}

		//`f32[N]` under N=8: a named extent takes the size the map carries; any other name waits for the binder.
		private static TypeName SubstExtents(TypeName type, Dictionary<string, TypeName> map)
		{
			if (type.Extents == null || !type.Extents.Any(i => i != null && map.ContainsKey(i)))
				return type;

			TypeName folded = Clone(type);
			for (int i = 0; i < folded.Dimensions.Count; i++)
			{
				string name = folded.Extents[i];
				if (name == null || !map.TryGetValue(name, out TypeName sized) || !AllDigits(sized.Name))
					continue;

				folded.Dimensions[i] = int.Parse(sized.Name);
				folded.Extents[i] = null;
			}

			if (folded.Extents.All(i => i == null))
				folded.Extents = null;

			folded.Name = $"{folded.ElementType}[{string.Join(",", folded.Written())}]";
			return folded;
		}

		private static TypeName Clone(TypeName type)
		{
			return new TypeName
			{
				Name = type.Name,
				IsArray = type.IsArray,
				IsAuto = type.IsAuto,
				Dimensions = [.. type.Dimensions],
				Extents = type.Extents == null ? null : [.. type.Extents],
				ElementType = type.ElementType,
				GenericType = type.GenericType,
				Generics = type.Generics?.Select(Clone).ToList(),
				Measure = type.Measure,
				MeasureBase = type.MeasureBase,
				Region = type.Region
			};
		}

		//Extents from the written type, element from the substitution; a named extent rides until the binder folds it.
		private static TypeName ArrayOf(TypeName type, TypeName element)
		{
			string extents = type.IsAuto ? "auto" : string.Join(",", type.Written());
			return new TypeName
			{
				Name = $"{element.Name}[{extents}]",
				IsArray = true,
				IsAuto = type.IsAuto,
				Dimensions = [.. type.Dimensions],
				Extents = type.Extents == null ? null : [.. type.Extents],
				ElementType = element.Name,
				Region = element.Region
			};
		}

		//Substitute the type map through a body and report every call; bottom-up, so arguments precede their call.
		private static void WalkStatements(List<Statement> body, Action<Call> onCall, Dictionary<string, TypeName> map, List<Message> messages)
		{
			for (int i = 0; i < body.Count; i++)
				body[i] = (Statement)body[i].Rewrite(node => Substitute(node, onCall, map, messages));
		}

		//Every TypeName a node carries is found by property, so a new node type cannot be missed the way a switch arm could.
		private static Node Substitute(Node node, Action<Call> onCall, Dictionary<string, TypeName> map, List<Message> messages)
		{
			foreach (PropertyInfo property in TypeNameProperties(node.GetType()))
			{
				switch (property.GetValue(node))
				{
					case TypeName single: property.SetValue(node, Rewrite(single, map, messages)); break;
					case List<TypeName> many: property.SetValue(node, many.Select(i => Rewrite(i, map, messages)).ToList()); break;
				}
			}

			//A literal carries its type in its own shape, and a call still has to be reported for instantiation.
			switch (node)
			{
				case Call x: onCall?.Invoke(x); break;
				case Value x: x.Literal = SubstLiteral(x.Literal, map); break;

				//A parameter in an expression: `N` (a bound, an extent) becomes the size itself, and `T` heading an allocation (`T[N]`) takes the substituted type's name.
				case Variable x when map.TryGetValue(x.SymbolName, out TypeName sized):
					if (AllDigits(sized.Name))
						return new Value
						{
							Literal = new IntLiteral { Value = int.Parse(sized.Name), TypeName = new TypeName { Name = "i32" } },
							Region = x.Region,
						};

					x.SymbolName = sized.Name;
					break;
			}

			return node;
		}
	}
}
