using System.Diagnostics;
using System.Text;

// Usage: dotnet run --project Cell.Launcher [-- name1 name2 …]
// Without arguments: the router and the cells c1, c2, c3. The cell c4 is "in reserve": start it separately
// (dotnet run --project Cell.Launcher -- c4), then register it with the control plane (POST /platform/cells).

Console.OutputEncoding = Encoding.UTF8;

(string Name, string Project, int Port, string Arguments, ConsoleColor Color)[] processes =
[
    ("c1", "Cell.App", 5231, "--Cell:Id=c1", ConsoleColor.Cyan),
    ("c2", "Cell.App", 5232, "--Cell:Id=c2", ConsoleColor.Yellow),
    ("c3", "Cell.App", 5233, "--Cell:Id=c3", ConsoleColor.Magenta),
    ("c4", "Cell.App", 5234, "--Cell:Id=c4", ConsoleColor.Blue),
    ("router", "Cell.Router", 5117, "", ConsoleColor.Green)
];

var chosen = args.Length == 0 ? processes.Where(p => p.Name != "c4").ToArray() : processes.Where(p => args.Contains(p.Name)).ToArray();
if (chosen.Length == 0)
{
    Console.WriteLine($"Known processes: {string.Join(", ", processes.Select(p => p.Name))}");
    return 1;
}

// bin/<Configuration>/<tfm>/ of the launcher → same configuration and tfm for the other projects.
var binaryFolder = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
var tfm = binaryFolder.Name;
var configuration = binaryFolder.Parent!.Name;
var root = binaryFolder.Parent.Parent!.Parent!.Parent!.FullName;

var started = new List<Process>();
var consoleLock = new Lock();

void Write(string name, ConsoleColor color, string? line)
{
    if (line is null) return;
    lock (consoleLock)
    {
        Console.ForegroundColor = color;
        Console.Write($"[{name,-8}] ");
        Console.ResetColor();
        Console.WriteLine(line);
    }
}

foreach (var (name, project, port, arguments, color) in chosen)
{
    var projectFolder = Path.Combine(root, project);
    var dll = Path.Combine(projectFolder, "bin", configuration, tfm, $"{project}.dll");
    if (!File.Exists(dll))
    {
        Console.WriteLine($"Not found: {dll}. Build the solution first.");
        return 1;
    }

    var info = new ProcessStartInfo("dotnet", $"\"{dll}\" --urls http://localhost:{port} {arguments}")
    {
        WorkingDirectory = projectFolder,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    info.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "false";

    var p = new Process { StartInfo = info, EnableRaisingEvents = true };
    p.OutputDataReceived += (_, e) => Write(name, color, e.Data);
    p.ErrorDataReceived += (_, e) => Write(name, color, e.Data);
    p.Exited += (_, _) => Write(name, color, "process stopped.");
    p.Start();
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    started.Add(p);
    Write(name, color, $"started on http://localhost:{port} (pid {p.Id})");
}

Console.WriteLine();
Console.WriteLine("Customer entry point: http://localhost:5117 (router). Ctrl+C to stop everything.");
Console.WriteLine();

var end = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    end.TrySetResult();
};
await end.Task;

foreach (var p in started.Where(p => !p.HasExited))
    p.Kill(entireProcessTree: true);
foreach (var p in started)
    await p.WaitForExitAsync();

return 0;
