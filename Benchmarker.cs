/* Run the compilation benchmark */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace CompilerBenchmarker
{
	static class StringExtensions
	{
		public static string Join(this IEnumerable<string> s, string delimiter) =>
			string.Join(delimiter, s);
	}

	class CompilerComparer : IComparer<Compiler>, IEqualityComparer<Compiler>
	{
		public int Compare(Compiler left, Compiler right)
		{
			var sc = StringComparer.OrdinalIgnoreCase;
			var exeCompared = sc.Compare(left.Exe, right.Exe);
			if (exeCompared == 0)
			{
				var leftArgs = string.Join("", left.OptimizeArguments);
				var rightArgs = string.Join("", right.OptimizeArguments);
				return sc.Compare(leftArgs, rightArgs);
			}

			return exeCompared;
		}

		public bool Equals(Compiler left, Compiler right) =>
			Compare(left, right) == 0;

		public int GetHashCode(Compiler c)
		{
			var h = 4021 ^ c.Language.GetHashCode() ^ c.Exe.GetHashCode();
			return c.OptimizeArguments == null ? h :h ^ c.OptimizeArguments.GetHashCode();
		}
	}

	class Compiler
	{
		// The name of the language, e.g. "C"
		public string Language;
		// The extension of the files in this language, e.g. "c"
		public string Extension;
		// The compiler executable, e.g. "gcc"
		public string Exe;
		// The arguments to the compiler executabe, e.g. "-O"
		public string OptimizeArguments;
		public string VersionArgument;
		public string MiscArguments;
		public string Version;
		public string VersionTrimmed;

		public Compiler(
			string language, string extension, string exe,
			string versionArgument,
			string optimizeArguments = null,
			string miscArguments = null)
		{
			if (string.IsNullOrWhiteSpace(language))
				throw new ArgumentNullException(nameof(language));
			if (string.IsNullOrWhiteSpace(extension))
				throw new ArgumentNullException(nameof(extension));
			if (string.IsNullOrWhiteSpace(exe))
				throw new ArgumentNullException(nameof(exe));
			if (string.IsNullOrWhiteSpace(versionArgument))
				throw new ArgumentNullException(nameof(versionArgument), "Missing option to determine compiler version");

			Language = language;
			Extension = extension;
			Exe = exe;
			VersionArgument = versionArgument;
			OptimizeArguments = optimizeArguments;
			MiscArguments = miscArguments;
			CheckCompilerAndSetVersion();
		}

		public override string ToString()
		{
			return OptimizeArguments != null
				? $"{Language} ({Exe} {OptimizeArguments}) [{VersionTrimmed}]"
				: $"{Language} ({Exe}) [{VersionTrimmed}]";
		}

		public string ToVerboseString() => ToString() + " " + Version;

		void CheckCompilerAndSetVersion()
		{
			using (var p = new Process())
			{
				p.StartInfo.FileName = Exe;
				p.StartInfo.Arguments = VersionArgument;
				p.StartInfo.UseShellExecute = false;
				p.StartInfo.RedirectStandardOutput = true;
				p.StartInfo.RedirectStandardError = true;
				p.Start();
				var sout = p.StandardOutput.ReadToEnd();
				var serr = p.StandardError.ReadToEnd();
				var o = string.IsNullOrWhiteSpace(sout) ? serr : sout;
				if (o == null)
					return;

				var line = o.Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)).FirstOrDefault()?.Trim();
				if (line == null)
					return;

				var r = new System.Text.RegularExpressions.Regex(@"(v?\d[\d\w.-]+)");
				var m = r.Match(line);
				Version = line;
				VersionTrimmed = m.Success ? m.Captures.First().Value : o;
			}
		}
	}

	class CompilerBenchmark
	{
		public Compiler Compiler;
		public TimeSpan TimeToCompile;
		public bool Compiled;
		public int NumberFunctions;
		public string SecondsToCompile =>
			Compiled ? TimeToCompile.TotalSeconds.ToString() : "";

		public static CompilerBenchmark Success(Compiler compiler, TimeSpan timeToCompile, int numberFunctions)
		{
			if (timeToCompile == TimeSpan.Zero)
				throw new ArgumentException("Compiling cannot take zero seconds", nameof(timeToCompile));
			return new CompilerBenchmark(compiler, timeToCompile, numberFunctions, true);
		}

		public static CompilerBenchmark Failure(Compiler compiler, int numberFunctions)
			=> new CompilerBenchmark(compiler, TimeSpan.Zero, numberFunctions, false);

		private CompilerBenchmark(Compiler compiler, TimeSpan timeToCompile, int numberFunctions, bool compiled)
		{
			if (numberFunctions < 0)
				throw new ArgumentException("Cannot compile zero functions", nameof(numberFunctions));

			Compiler = compiler;
			TimeToCompile = timeToCompile;
			NumberFunctions = numberFunctions;
			Compiled = compiled;
		}
	}

	static class Benchmarker
	{
		static TimeSpan? RunBenchmark(Compiler compiler, string codeFilePath, int numFun)
		{
			var watch = new Stopwatch();
			watch.Start();
			var args = compiler.MiscArguments + " " + compiler.OptimizeArguments + " " + codeFilePath;
			Console.WriteLine($"  - Running with {numFun}: {compiler.Exe} {args}");
			using (var p = Process.Start(compiler.Exe, args))
			{
				// todo: pass in compiler timeout option
				p.WaitForExit();
				if (p.ExitCode != 0)
				{
					watch.Stop();
					Console.WriteLine($"  ! Compilation failed for '{compiler.Exe} {args}'");
					Thread.Sleep(2500);
					return null;
				}
			}

			// todo: also track memory consumption
			watch.Stop();
			Console.WriteLine($"  - Took {watch.Elapsed}");
			Console.WriteLine();
			return watch.Elapsed;
		}

		static IEnumerable<CompilerBenchmark> RunBenchmarks(
			List<Compiler> compilers, int numberAtStart, int numberOfSteps, int increaseOnStep)
		{
			var codeGen = new CodeGen();
			var failed = new HashSet<Compiler>();
			// todo: record compiler failure reason
			foreach (var langCompilers in compilers.GroupBy(x => x.Language))
			{
				Console.WriteLine($"Benchmarking {langCompilers.Key}:");
				for (int numFun = numberAtStart, step = 1;
					step <= numberOfSteps;
					step += 1, numFun += increaseOnStep)
				{
					// generate file
					Console.Write($"- Generating {langCompilers.Key} with {numFun} functions.. ");
					// todo: don't use existing files by default, just have the option to
		            var codeFilePath = $"test_{numFun}.{langCompilers.First().Extension}";
		            if (File.Exists(codeFilePath))
		            {
		            	Console.Write("Exists already.");
		            }
		            else
		            {
						codeGen.WriteLang(langCompilers.Key, numFun, codeFilePath);
		            }
	            	Console.WriteLine();

					foreach (var compiler in langCompilers)
					{
						// run benchmark
						if (failed.Contains(compiler))
							yield return CompilerBenchmark.Failure(compiler, numFun);
						var bench = RunBenchmark(compiler, codeFilePath, numFun);
						if (!bench.HasValue)
							failed.Add(compiler);
						yield return bench.HasValue
							? CompilerBenchmark.Success(compiler, bench.Value, numFun)
							: CompilerBenchmark.Failure(compiler, numFun);
					}

					// todo: pass in file cleanup options
				}
			}
		}

		static void WriteResults(
			IEnumerable<Compiler> compilersUsed,
			IEnumerable<CompilerBenchmark> marks,
			string resultFileName)
		{
			var compilerComp = new CompilerComparer();

			// [Number of Functions -> { Compiler -> Benchmark }]
			var rowData = marks
				.GroupBy(x => x.NumberFunctions)
				.Select(x => new {
					N = x.Key,
					M = x.ToDictionary(y => y.Compiler, compilerComp)
				});

			var first = rowData.First();
			var header = new List<string> { "Number Functions" }
				.Concat(first.M.Select(x => x.Key.ToString()))
				.Join(", ");

			var rows = rowData
				.Select(x => new List<string> { x.N.ToString() }
					.Concat(x.M.Select(y => y.Value.SecondsToCompile))
					.Join(", ")
				);

			var filetext = string.Join("\n", new List<string> { header }.Concat(rows));
			File.WriteAllText(resultFileName, filetext);
			Console.WriteLine($"Wrote benchmark results to {Path.GetFullPath(resultFileName)}");
		}

		static void Main(string[] args)
		{
			int numberAtStart = 20;
			int numberOfSteps = 1;
			int stepIncreaseNumber = 5000;

			// todo: good command-line options library for C#?
			var helpInfo = new Dictionary<string, int>
			{
				{ "numberAtStart", numberAtStart },
				{ "numberOfSteps", numberOfSteps },
				{ "increaseOnStep", stepIncreaseNumber }
				// options todo:
				// - cleanup on/off (on by default)
				// - csv file name (timestamped default)
				// - additional compilers or options somehow
			};

			try
			{
				var compilers = new List<Compiler>
				{
					// Native
					new Compiler("C",         "c",      "gcc", "--version", "-O2"),
					new Compiler("C++",     "cpp",      "g++", "--version", "-O2"),
					new Compiler("C++",     "cpp",    "clang", "--version", "-O2"),
					new Compiler("Go",       "go",       "go",   "version", "build"),
					new Compiler("Rust",     "rs",    "rustc", "--version", "-C opt-level=2"),
					new Compiler("D",         "d",      "dmd", "--version", "-O"),
					new Compiler("D",         "d",      "gdc", "--version", "-O"),
					new Compiler("D",         "d",     "ldc2", "--version", "-O"),
					new Compiler("Haskell",  "hs",      "ghc", "--version", "-O"),
					new Compiler("OCaml",    "ml", "ocamlopt", "--version", "-O2"),
					// VM
					new Compiler("CSharp",   "cs",      "csc", "/version", "/o", miscArguments: "/nowarn:1717"),
					new Compiler("FSharp",   "fs",  "fsharpc",   "--help", "-O", miscArguments: "--nologo"), // fsharpc does not have a version flag?
					new Compiler("Java",   "java",    "javac", "-version",       miscArguments: "-J-Xmx4096M -J-Xms64M"),
					new Compiler("Scala", "scala",   "scalac", "-version", "-optimise"), // modified to use Java -Xmx4096M -Xms64M -Xss4m
					new Compiler("Scala", "scala",     "dotc", "-version", "-optimise"), // modified to use Java -Xmx4096M -Xss4m
					new Compiler("Kotlin",   "kt",  "kotlinc", "-version"),              // modified to use Java -Xmx4096M -Xms64M -Xss4m
				};

				foreach (var c in compilers.GroupBy(x => x.Exe).Select(x => x.First()))
				{
					Console.WriteLine($"Found compiler: {c.Exe} ::: {c.Version}");
				}

				Console.WriteLine("\n");

				// todo: verify compilers exist on system
				// todo: write hardware/software report (compiler version, OS, Kernel, CPU, Memory, HD)
				// todo: duplicate compiler detection
				// todo: Ctrl+C writes results so far
				// todo: other keys to skip/abort language/number functions?
				// todo: compiler timeout feature?
				// todo: total timeout feature?
				// todo: pass in this option, enforce it is in bin at least if inside

				var home = Environment.GetEnvironmentVariable("HOME");
				var write_to = $"{home}/testfiles";
				if (!Directory.Exists(write_to))
                	Directory.CreateDirectory(write_to);

            	Directory.SetCurrentDirectory(write_to);

            	var baseFileName = $"results_{DateTime.Now.ToString("yyyyMMddTHHmmss")}";
				var systemInfoFileName = $"{DateTime.Now.ToString("yyyyMMdd")}_systemInfo.txt";
            	var ongoingResultsFileName = $"{baseFileName}_ongoing.csv";
				var finalResultFileName = $"{baseFileName}_final.csv";
				if (!File.Exists(systemInfoFileName))
				{
					var info = BasicSystemInfo.Find();
					var infoText = new[] { info.OS, info.CPU, info.Memory }.Join("\n\n");
					File.WriteAllText(systemInfoFileName, infoText);
				}
				if (File.Exists(ongoingResultsFileName))
					File.Delete(ongoingResultsFileName);
				if (File.Exists(finalResultFileName))
					File.Delete(finalResultFileName);

				var allBenchmarks = new List<CompilerBenchmark>(compilers.Count * 100);
				using (var ongoing = File.AppendText(ongoingResultsFileName))
				{
					ongoing.WriteLine($"Compiler, Number Functions, Time");

					var benchmarks = RunBenchmarks(compilers, numberAtStart, numberOfSteps, stepIncreaseNumber);
					foreach (var b in benchmarks)
					{
						allBenchmarks.Add(b);
						ongoing.WriteLine($"{b.Compiler.ToString()}, {b.NumberFunctions}, {b.SecondsToCompile}");
					}
				}

				WriteResults(compilers, allBenchmarks, finalResultFileName);
			}
			catch (Exception e)
			{
				Console.WriteLine(e.ToString());
			}
		}
	}
}