using System.Diagnostics;

const string TestDir = "Tests";
const string BuildDir = @$"{TestDir}\build";
const string Compiler = @"Src\Orion\bin\Debug\net8.0\Orion.exe";
const string Msvc = "cl.exe";
const string Python = "python3";
const string PythonLibrary = @"Library\";

if (!Directory.Exists(BuildDir))
	Directory.CreateDirectory(BuildDir);

static bool Run(string path, out string output, string args = null, Dictionary<string, string> env = null)
{
	Console.WriteLine($"Running: {path} {args}");
	
	ProcessStartInfo startInfo = new ProcessStartInfo
	{
		FileName = path,
		Arguments = args,
		RedirectStandardOutput = true,
		UseShellExecute = false,
	};

	//Add env
	if (env != null)
	{
		foreach (KeyValuePair<string, string> kvp in env)
		{
			startInfo.EnvironmentVariables.Add(kvp.Key, kvp.Value);
		}
	}

	Process process = Process.Start(startInfo);
	output = process.StandardOutput.ReadToEnd();
	//Console.WriteLine(output);
	process.WaitForExit();
	bool success = process.ExitCode == 0;
	if (!success)
		Console.WriteLine(output);

	return success;
}

static void Assert(bool condition)
{
	if (!condition)
		Environment.Exit(1);
}

static void AssertEqual(string expected, string actual)
{
	if (expected != actual)
	{
		Console.WriteLine($"Expected:\n{expected}");
		Console.WriteLine($"Actual:\n{actual}");

		Console.WriteLine("Outputs dont match.");
		Assert(false);
	}
	else
	{
		Console.WriteLine("Outputs match.");
	}
}

static void RunCppTest(string test)
{
	Console.WriteLine($"Test: {test}");

	string srcFile = Path.Combine($"{TestDir}\\{test}.src");
	string expectedFile = Path.Combine($"{TestDir}\\{test}.txt");
	string cppFile = Path.Combine(BuildDir, test + ".cpp");
	string exeFile = Path.Combine(BuildDir, test + ".exe");

	string expected = File.ReadAllText(expectedFile);
	string output;

	Assert(Run(Compiler, out output, $"compile {srcFile} -o {cppFile} -v -l cpp -j"));
	Assert(Run(Msvc, out output, @$"{cppFile} -ILibrary /Fo:{BuildDir}\ /Fe:{BuildDir}\ /EHsc /std:c++20"));
	Assert(Run(exeFile, out output));
	AssertEqual(expected, output);
}

static void RunPythonTest(string test)
{
	Console.WriteLine($"Test: {test}");

	string srcFile = Path.Combine($"{TestDir}\\{test}.src");
	string expectedFile = Path.Combine($"{TestDir}\\{test}.txt");
	string pythonFile = Path.Combine(BuildDir, test + ".py");
	string exeFile = Path.Combine(BuildDir, test + ".exe");

	string expected = File.ReadAllText(expectedFile);
	string output;
	Assert(Run(Compiler, out output, $"compile {srcFile} -o {pythonFile} -l python"));
	Assert(Run(Python, out output, pythonFile, env: new Dictionary<string, string> { {"PYTHONPATH", PythonLibrary} }));
	AssertEqual(expected, output);
}

//Print python version
string output = string.Empty;
Run(Python, out output, "--version");
Console.WriteLine(output);

string[] files = Directory.GetFiles(TestDir, "*.src");
//string[] files = new string[] { "solver_simple.src" };
foreach (string f in files)
{
	string fileName = Path.GetFileNameWithoutExtension(f);
	RunCppTest(fileName);
	RunPythonTest(fileName);
}
