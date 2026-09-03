using Orion.Ast;
using Orion.Clr;
using Orion.Diagnostics;
using Orion.Frontend;
using Orion.Symbols;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System;

namespace Orion.BuildTime.Builtins
{
	//Loads a whole other source tree mid-build for `#src`, binding it into its own child scope.
	[BuildOnly]
	internal static class SrcLoader
	{
		private static Dictionary<string, MethodInfo> _loaded => Compiler.Session.SrcLoaded;

		private static HashSet<string> _active => Compiler.Session.SrcActive;

		private static int _scopes { get => Compiler.Session.SrcScopes; set => Compiler.Session.SrcScopes = value; }

		internal static T Stopped<T>(string path, string entry)
		{
			if (!Env.Context.Messages.HasError())
				Env.Report($"#src \"{path}\" {entry}: produced no value.");

			throw new BuildStoppedException();
		}

		internal static object Invoke(string path, string name, object args)
		{
			List<Message> messages = Env.Context.Messages;

			string full = Path.IsPathRooted(path) || string.IsNullOrEmpty(Compiler.Session.Root)
				? Path.GetFullPath(path)
				: Path.GetFullPath(Path.Combine(Compiler.Session.Root, path));

			string key = full + "::" + name;
			if (!_active.Add(key))
			{
				messages.Add(new Message($"#src \"{path}\" {name}: circular #src -- this entry is already loading or running.", Env.Region, MessageType.Error));
				return null;
			}

			try
			{
				if (!_loaded.TryGetValue(key, out MethodInfo entry))
				{
					entry = Load(full, name, messages);
					if (entry == null)
						return null;
					_loaded[key] = entry;
				}

				Dictionary<string, object> values = args as Dictionary<string, object> ?? new Dictionary<string, object>();
				List<object> call = new List<object>();
				foreach ((ParameterInfo param, int index) in entry.GetParameters().Select((p, i) => (p, i)))
				{
					if (!values.TryGetValue(param.Name, out object value) && !values.TryGetValue($"${index}", out value))
					{
						messages.Add(new Message($"#src \"{path}\" {name}: missing argument '{param.Name}'.", Env.Region, MessageType.Error));
						return null;
					}
					//Invariant culture: `#config` argument coercion must not read `1.5` per the OS locale.
					try
					{
						call.Add(Convert.ChangeType(value, param.ParameterType, System.Globalization.CultureInfo.InvariantCulture));
					}
					catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
					{
						messages.Add(new Message($"#src \"{path}\" {name}: argument '{param.Name}' does not convert to {param.ParameterType.Name}.", Env.Region, MessageType.Error));
						return null;
					}
				}

				try
				{
					return entry.Invoke(null, call.ToArray());
				}
				catch (TargetInvocationException ex)
				{
					Exception inner = ex.InnerException ?? ex;
					messages.Add(new Message($"#src \"{path}\" {name}: threw {inner.GetType().Name}: {inner.Message}", Env.Region, MessageType.Error));
					return null;
				}
			}
			finally
			{
				_active.Remove(key);
			}
		}

		private static MethodInfo Load(string full, string name, List<Message> messages)
		{
			if (!System.IO.File.Exists(full))
			{
				messages.Add(new Message($"#src file not found: {full}", Env.Region, MessageType.Error));
				return null;
			}

			SymbolTable root = Env.Context.Function.Table.GetRoot();

			List<FileBlock> blocks = new List<FileBlock>();
			if (!Gather(full, blocks, messages))
				return null;

			List<Ast.Function> entries = blocks.OfType<Ast.Function>().Where(f => f.Name == name).ToList();
			if (entries.Count != 1)
			{
				messages.Add(new Message($"#src file {full} must define exactly one `{name}` function, found {entries.Count}.", Env.Region, MessageType.Error));
				return null;
			}
			if (!entries[0].IsBuild)
			{
				messages.Add(new Message($"#src entry `{name}` in {full} must be a #build function.", Env.Region, MessageType.Error));
				return null;
			}

			TranslationUnit unit = new TranslationUnit { Blocks = blocks };

			//The chain may declare generics: templates extract and calls instantiate before binding, as the compiler's table does.
			Monomorphizer.ExpandIsolated(unit, messages);

			//After expansion, so an instantiation the outer compile already bound is reused rather than redeclared.
			unit.Blocks = unit.Blocks.Where(b => !Bound(root, b)).ToList();

			SymbolTable scope = root.CreateChild($"src${_scopes++}${name}");
			if (!Pipeline.Lower(unit, scope, messages, emit: true))
				return null;

			BuildAssembly.Close();

			return unit.Blocks.OfType<Ast.Function>().FirstOrDefault(i => i.Name == name)?.Symbol.Info;
		}

		private static bool Bound(SymbolTable root, FileBlock block)
		{
			switch (block)
			{
				case Struct s: return root.TryGet(s.Name, out TypeSymbol _);
				case Ast.Enum e: return root.TryGet(e.Name, out TypeSymbol _);
				case Ast.Function f: return root.TryGet(f.Name, out FunctionSymbol _);
				case Const c: return root.TryGet(c.Name, out NamedDataSymbol _);
				default: return true;
			}
		}

		private static bool Gather(string entry, List<FileBlock> blocks, List<Message> messages)
		{
			foreach (CompilerFile file in Parsing.GatherAsts(entry, messages))
			{
				Desugar.Run(file.Ast, messages);
				blocks.AddRange(file.Ast.Blocks);
			}

			return !messages.HasError();
		}
	}
}
