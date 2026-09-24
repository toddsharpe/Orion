using System.Collections.Generic;

namespace Orion.Tests.Frontend
{
	//A typedef names a primitive's representation; a struct or enum declared anywhere in the unit is refused as one, not as an unknown type.
	[TestClass]
	public class TypedefTest
	{
		[TestMethod]
		[DataRow("struct P { i32 x; }\ntypedef P Q;\n", "Typedef Q names `P`, which is not a primitive.")]
		[DataRow("typedef E F;\nenum E { A }\n", "Typedef F names `E`, which is not a primitive.")]
		public void ATypedefOfANonPrimitiveIsOneError(string declarations, string expected)
		{
			List<string> errors = Harness.Compile(declarations + "i32 main()\n{\n\treturn 0;\n}\n").Errors();
			Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
			StringAssert.Contains(errors[0], expected);
		}
	}
}
