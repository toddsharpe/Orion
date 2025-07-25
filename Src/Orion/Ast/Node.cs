using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Orion.Ast
{
	public abstract class Node
	{
		public InputRegion Region { get; set; }
		internal abstract void Accept(IAstVisitor visitor);

		public IEnumerable<Node> GetChildren()
		{
			Type type = GetType();
			PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			foreach (PropertyInfo property in properties)
			{
				bool isList = property.PropertyType.IsGenericType &&
					property.PropertyType.GetGenericTypeDefinition() == typeof(List<>) &&
					property.PropertyType.GenericTypeArguments.Any(i => i.IsAssignableTo(typeof(Node)));
				object value = property.GetValue(this);
				if (isList)
				{
					foreach (Node item in (IEnumerable)value)
						yield return item;
				}
				else if (typeof(Node).IsAssignableFrom(property.PropertyType))
				{
					yield return value as Node;
				}
			}
		}

		public IEnumerable<(Node, Node)> GetChildrenWithParent()
		{
			foreach (Node node in GetChildren())
				yield return (this, node);
		}

		public IEnumerable<Node> DFS()
		{
			Stack<Node> stack = new Stack<Node>();
			stack.Push(this);

			while (stack.Count > 0)
			{
				Node current = stack.Pop();
				foreach (Node child in current.GetChildren())
					stack.Push(child);

				yield return current;
			}
		}
	}
}
