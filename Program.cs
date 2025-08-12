using System.Diagnostics;
using System.Text;

public enum Scenario
{
    Empty,
    Funcs,
    CppMember, FreeFunc,
    CppOverload, NoOverload,
    ReturnByValue, ReturnByPointer,
}

public class BenchmarkOptions
{
    public int N { get; set; } = 1000;
    public int[] NValues { get; set; } = Array.Empty<int>();
    public bool GenOnly { get; set; } = false;
    public string Compiler { get; set; } = "msvc";
    public Scenario Scenario { get; set; } = Scenario.Funcs;
    public int Runs { get; set; } = 5;
    public bool Quiet { get; set; } = false;
    public bool O2 { get; set; } = false;
    public bool UseCpp { get; set; } = false;
    
    public int[] GetNValuesToTest() => NValues.Length > 0 ? NValues : new[] { N };
}

public class BenchmarkResult
{
    public Scenario Scenario { get; set; }
    public string Compiler { get; set; }
    public bool O2 { get; set; }
    public bool UseCpp { get; set; }
    public int N { get; set; }
    public double[] Times { get; set; } = Array.Empty<double>();
    public double Average => Times.Length > 0 ? Times.Average() : 0;
    public double Min => Times.Length > 0 ? Times.Min() : 0;
    public double Max => Times.Length > 0 ? Times.Max() : 0;
    public double Median
    {
        get
        {
            if (Times.Length == 0) return 0;
            var sorted = Times.OrderBy(x => x).ToArray();
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
        }
    }
    public double StdDev
    {
        get
        {
            if (Times.Length <= 1) return 0;
            double mean = Average;
            double sumSquaredDiffs = Times.Sum(x => Math.Pow(x - mean, 2));
            return Math.Sqrt(sumSquaredDiffs / (Times.Length - 1));
        }
    }
    public string CsvFileName { get; set; } = string.Empty;
}

public class CompilationBenchmark
{
    public BenchmarkResult RunBenchmark(BenchmarkOptions options)
    {
        bool isCpp = options.UseCpp || options.Scenario.ToString().StartsWith("Cpp");
        string fileName = isCpp ? "test.cpp" : "test.c";
        string code = GenerateCode(options.Scenario, options.N);

        var result = new BenchmarkResult
        {
            Scenario = options.Scenario,
            Compiler = options.Compiler,
            O2 = options.O2,
            UseCpp = isCpp,
            N = options.N
        };

        if (options.GenOnly)
        {
            File.WriteAllText(fileName, code);
            return result;
        }

        if (!Directory.Exists("output"))
            Directory.CreateDirectory("output");

        var msvcEnv = FindMsvcEnv();

        string baseDir = Path.Combine(Path.GetTempPath(), "CompileTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);

        string filePath = Path.Combine(baseDir, fileName);
        File.WriteAllText(filePath, code);

        const string ObjFileName = "test.obj";
        string batchFile = Path.Combine(baseDir, "run_compile.bat");
        string compilerExe = options.Compiler == "clang" ? "clang-cl" : "cl";
        string optFlags = options.O2 ? "/O2" : "/Od";
        string compileCmd = $"{compilerExe} /nologo {optFlags} /c {fileName} /Fo{ObjFileName}";

        using (var w = new StreamWriter(batchFile, false, Encoding.ASCII))
        {
            w.WriteLine("@echo off");
            w.WriteLine($"call \"{msvcEnv.vcvarsPath}\" {msvcEnv.arch}");
            w.WriteLine($"for /L %%i in (1,1,{options.Runs}) do (");
            w.WriteLine($"  if exist \"{ObjFileName}\" del /f /q \"{ObjFileName}\"");
            w.WriteLine($"  echo Run %%i:");
            w.WriteLine($"  powershell -NoProfile -Command \" $t=Measure-Command {{ {compileCmd} }}; Write-Output $t.TotalSeconds\"");
            w.WriteLine("  if errorlevel 1 (");
            w.WriteLine("    echo Compile failed with error %ERRORLEVEL%");
            w.WriteLine("    exit /b %ERRORLEVEL%");
            w.WriteLine("  )");
            w.WriteLine($"  if not exist \"{ObjFileName}\" (");
            w.WriteLine($"    echo Output file {ObjFileName} not found!");
            w.WriteLine("    exit /b 1");
            w.WriteLine("  )");
            w.WriteLine(")");
        }

        var psi = new ProcessStartInfo(batchFile)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = options.Quiet,
            UseShellExecute = false,
            CreateNoWindow = options.Quiet,
            WorkingDirectory = baseDir
        };

        var times = new double[options.Runs];
        int runIndex = -1;
        using (var proc = Process.Start(psi))
        using (var sr = proc.StandardOutput)
        {
            while (!sr.EndOfStream)
            {
                var line = sr.ReadLine().Trim();
                if (line.StartsWith("Run "))
                {
                    runIndex++;
                    if (!options.Quiet) Console.WriteLine(line);
                    continue;
                }
                if (runIndex >= 0 && double.TryParse(line, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t))
                {
                    times[runIndex] = t;
                    if (!options.Quiet)
                        Console.WriteLine($"Run {runIndex + 1} timing: {t:F3}s");
                }
            }
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                throw new Exception($"Batch process exited with code {proc.ExitCode}");
        }

        Directory.Delete(baseDir, true);

        result.Times = times;
        result.CsvFileName = MakeCsvFileName(options.N, options.Compiler, options.Scenario, options.O2);

        if (!options.Quiet)
        {
            Console.WriteLine($"Average: {result.Average:F3}s, Median: {result.Median:F3}s, StdDev: {result.StdDev:F3}s, Min: {result.Min:F3}s, Max: {result.Max:F3}s");
        }

        using var wCsv = new StreamWriter(Path.Combine("output", result.CsvFileName));
        wCsv.WriteLine("Run,Seconds");
        for (int i = 0; i < options.Runs; i++)
            wCsv.WriteLine($"{i + 1},{times[i]:F6}");
        
        if (!options.Quiet)
            Console.WriteLine($"Timings saved to {result.CsvFileName}");

        return result;
    }

    public List<BenchmarkResult> RunAllScenarios(BenchmarkOptions baseOptions)
    {
        var results = new List<BenchmarkResult>();
        var scenarios = Enum.GetValues<Scenario>();

        var nValues = baseOptions.GetNValuesToTest();
        Console.WriteLine($"Running all scenarios with n values: [{string.Join(", ", nValues)}], compiler={baseOptions.Compiler}, runs={baseOptions.Runs}");
        Console.WriteLine(new string('=', 80));

        foreach (var n in nValues)
        {
            Console.WriteLine($"\nTesting with N = {n}");
            Console.WriteLine(new string('-', 40));

            foreach (var scenario in scenarios)
            {
                var options = new BenchmarkOptions
                {
                    N = n,
                    GenOnly = baseOptions.GenOnly,
                    Compiler = baseOptions.Compiler,
                    Scenario = scenario,
                    Runs = baseOptions.Runs,
                    Quiet = baseOptions.Quiet,
                    O2 = baseOptions.O2,
                    UseCpp = baseOptions.UseCpp
                };

                Console.WriteLine($"Running scenario: {scenario} (n={n})");
                try
                {
                    var result = RunBenchmark(options);
                    results.Add(result);

                    if (!baseOptions.Quiet)
                    {
                        Console.WriteLine($"  Average: {result.Average:F3}s, Median: {result.Median:F3}s, StdDev: {result.StdDev:F3}s, Min: {result.Min:F3}s, Max: {result.Max:F3}s");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error: {ex.Message}");
                }
                Console.WriteLine();
            }
        }

        return results;
    }

    public List<BenchmarkResult> RunScenarioWithAllVariations(BenchmarkOptions baseOptions)
    {
        var results = new List<BenchmarkResult>();
        var nValues = baseOptions.GetNValuesToTest();
        
        var variations = new[]
        {
            (cpp: false, o2: false, name: "C Od"),
            (cpp: false, o2: true, name: "C O2"),
            (cpp: true, o2: false, name: "C++ Od"),
            (cpp: true, o2: true, name: "C++ O2")
        };
        if (baseOptions.Scenario.ToString().StartsWith("Cpp"))
        {
            variations = new[]
            {
                (cpp: true, o2: false, name: "C++ Od"),
                (cpp: true, o2: true, name: "C++ O2")
            };  
        }

        Console.WriteLine($"Running scenario '{baseOptions.Scenario}' with all variations");
        Console.WriteLine($"N values: [{string.Join(", ", nValues)}], compiler={baseOptions.Compiler}, runs={baseOptions.Runs}");
        Console.WriteLine(new string('=', 80));

        foreach (var n in nValues)
        {
            Console.WriteLine($"\nTesting with N = {n}");
            Console.WriteLine(new string('-', 40));
            
            foreach (var (cpp, o2, name) in variations)
            {
                var options = new BenchmarkOptions
                {
                    N = n,
                    GenOnly = baseOptions.GenOnly,
                    Compiler = baseOptions.Compiler,
                    Scenario = baseOptions.Scenario,
                    Runs = baseOptions.Runs,
                    Quiet = baseOptions.Quiet,
                    O2 = o2,
                    UseCpp = cpp
                };

                Console.WriteLine($"Running {name} variation (n={n})");
                try
                {
                    var result = RunBenchmark(options);
                    results.Add(result);
                    
                    if (!baseOptions.Quiet)
                    {
                        Console.WriteLine($"  Average: {result.Average:F3}s, Median: {result.Median:F3}s, StdDev: {result.StdDev:F3}s, Min: {result.Min:F3}s, Max: {result.Max:F3}s");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error: {ex.Message}");
                }
                Console.WriteLine();
            }
        }

        return results;
    }

    public void GenerateComparisonReport(List<BenchmarkResult> results, string outputFile = "comparison_report.csv")
    {
        string outputPath = Path.Combine("output", outputFile);
        using var writer = new StreamWriter(outputPath);
        writer.WriteLine("Scenario,Compiler,Optimization,Mode,N,AverageSeconds,MedianSeconds,StdDevSeconds,MinSeconds,MaxSeconds,CsvFile");
        
        foreach (var result in results.OrderBy(r => r.N).ThenBy(r => r.Scenario).ThenBy(r => r.Compiler))
        {
            writer.WriteLine($"{result.Scenario},{result.Compiler},{(result.O2 ? "O2" : "Od")},{(result.UseCpp ? "cpp" : "c")},{result.N},{result.Average:F6},{result.Median:F6},{result.StdDev:F6},{result.Min:F6},{result.Max:F6},{result.CsvFileName}");
        }
        
        Console.WriteLine($"Comparison report saved to {outputPath}");
    }

    private static string GenerateCode(Scenario scenario, int n)
    {
        var sb = new StringBuilder();
        switch (scenario)
        {
            case Scenario.Funcs:
                sb.AppendLine("int f0(int x){return x;}");
                for (int i = 1; i < n; i++)
                    sb.AppendLine($"int f{i}(int x){{return f{i - 1}(x);}}");
                sb.AppendLine("int main(){");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"    f{i}(0);");
                sb.AppendLine("    return 0;");
                sb.AppendLine("}");
                break;

            case Scenario.CppMember:
                sb.AppendLine("struct S { int m(int x){return x;} };");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"int f{i}(int x){{S s; return s.m(x);}}");
                sb.AppendLine("int main(){");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"    f{i}(0);");
                sb.AppendLine("    return 0;");
                sb.AppendLine("}");
                break;

            case Scenario.FreeFunc:
                sb.AppendLine("typedef struct S { int v; } S;");
                sb.AppendLine("int call(S* s, int x) { return x; }");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"int f{i}(int x) {{ S s; return call(&s, x); }}");
                sb.AppendLine("int main() {");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"    f{i}(0);");
                sb.AppendLine("    return 0;");
                sb.AppendLine("}");
                break;

            case Scenario.Empty:
                return "";

            case Scenario.CppOverload:
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"struct S{i} {{ int v; }};");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"int f(S{i}* s) {{ return s->v; }}");
                sb.AppendLine("int main() {");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"    S{i} s{i}; f(&s{i});");
                sb.AppendLine("    return 0;");
                sb.AppendLine("}");
                break;
            case Scenario.NoOverload:
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"typedef struct {{ int v; }} S{i};");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"int f{i}(S{i}* s) {{ return s->v; }}");
                sb.AppendLine("int main() {");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"    S{i} s{i}; f{i}(&s{i});");
                sb.AppendLine("    return 0;");
                sb.AppendLine("}");
                break;
            case Scenario.ReturnByValue:
                sb.AppendLine("typedef struct S { int v; } S;");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"S f{i}() {{ S s = {{ {i} }}; return s; }}");
                sb.AppendLine("int main() {");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"    {{ S s = f{i}(); (void)s.v; }} ");
                sb.AppendLine("    return 0;");
                sb.AppendLine("}");
                break;

            case Scenario.ReturnByPointer:
                sb.AppendLine("typedef struct S { int v; } S;");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"S* f{i}() {{ static S s = {{ {i} }}; return &s; }}");
                sb.AppendLine("int main() {");
                for (int i = 0; i < n; i++)
                    sb.AppendLine($"    {{ S* s = f{i}(); (void)s->v; }} ");
                sb.AppendLine("    return 0;");
                sb.AppendLine("}");
                break;
        }
        return sb.ToString();
    }

    private static string MakeCsvFileName(int n, string compiler, Scenario scenario, bool o2)
    {
        string cleanScenario = scenario.ToString().ToLowerInvariant();
        string opts = o2 ? "o2" : "od";
        return $"timings_n{n}_compiler-{compiler}_{opts}_scenario-{cleanScenario}.csv";
    }

    private static (string vcvarsPath, string arch) FindMsvcEnv()
    {
        string vswherePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");

        if (!File.Exists(vswherePath))
            throw new Exception("vswhere.exe not found");

        string vsInstallPath = RunProcessCapture(vswherePath,
            "-latest -prerelease -products * " +
            "-requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 " +
            "-property installationPath").Trim();

        string vcvarsPath = Path.Combine(vsInstallPath, @"VC\Auxiliary\Build\vcvarsall.bat");
        if (!File.Exists(vcvarsPath))
            throw new Exception("vcvarsall.bat not found");

        return (vcvarsPath, "x64");
    }

    private static string RunProcessCapture(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi);
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }
}

class Program
{
    static void Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        var options = new BenchmarkOptions();
        bool allScenarios = false;
        bool allVariations = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-n":
                    if (i + 1 >= args.Length) throw new Exception("Missing value for -n");
                    var nArg = args[++i];
                    if (nArg.Contains(','))
                    {
                        options.NValues = nArg.Split(',').Select(int.Parse).ToArray();
                    }
                    else
                    {
                        options.N = int.Parse(nArg);
                    }
                    break;
                case "--genonly":
                    options.GenOnly = true;
                    break;
                case "--compiler":
                    if (i + 1 >= args.Length) throw new Exception("Missing value for --compiler");
                    options.Compiler = args[++i].ToLower();
                    break;
                case "--scenario":
                    if (i + 1 >= args.Length) throw new Exception("Missing value for --scenario");
                    options.Scenario = Enum.Parse<Scenario>(args[++i], true);
                    break;
                case "--runs":
                    if (i + 1 >= args.Length) throw new Exception("Missing value for --runs");
                    options.Runs = int.Parse(args[++i]);
                    break;
                case "--quiet":
                    options.Quiet = true;
                    break;
                case "--o2":
                    options.O2 = true;
                    break;
                case "--cpp":
                    options.UseCpp = true;
                    break;
                case "--all-scenarios":
                    allScenarios = true;
                    break;
                case "--n-values":
                    if (i + 1 >= args.Length) throw new Exception("Missing value for --n-values");
                    options.NValues = args[++i].Split(',').Select(int.Parse).ToArray();
                    break;
                case "--all-variations":
                    allVariations = true;
                    break;
                default:
                    throw new Exception($"Unknown argument: {args[i]}");
            }
        }

        var benchmark = new CompilationBenchmark();

        if (allScenarios)
        {
            var results = benchmark.RunAllScenarios(options);
            benchmark.GenerateComparisonReport(results);
        }
        else if (allVariations)
        {
            var results = benchmark.RunScenarioWithAllVariations(options);
            string fileName = $"{options.Compiler}_{options.Scenario.ToString().ToLowerInvariant()}.csv";
            benchmark.GenerateComparisonReport(results, fileName);
        }
        else
        {
            benchmark.RunBenchmark(options);
        }
    }
}