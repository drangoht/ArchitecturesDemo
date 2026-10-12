using System.Diagnostics;
using System.Text;

// Usage: dotnet run --project Micro.Launcher [-- service1 service2 …]
// Without arguments, all the services start. With names, only those
// (handy to stop a service and watch how the others behave).

Console.OutputEncoding = Encoding.UTF8;

(string Name, string Project, int Port, ConsoleColor Color)[] services =
[
    ("broker", "Micro.Broker", 5209, ConsoleColor.DarkGray),
    ("accounts", "Micro.Accounts", 5201, ConsoleColor.Cyan),
    ("transfers", "Micro.Transfers", 5202, ConsoleColor.Yellow),
    ("notifications", "Micro.Notifications", 5203, ConsoleColor.Magenta),
    ("gateway", "Micro.Gateway", 5200, ConsoleColor.Green)
];

var chosen = args.Length == 0 ? services : services.Where(s => args.Contains(s.Name)).ToArray();
if (chosen.Length == 0)
{
    Console.WriteLine($"Known services: {string.Join(", ", services.Select(s => s.Name))}");
    return 1;
}

// bin/<Configuration>/<tfm>/ of the launcher → same configuration and tfm for the services.
var binaryFolder = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
var tfm = binaryFolder.Name;
var configuration = binaryFolder.Parent!.Name;
var root = binaryFolder.Parent.Parent!.Parent!.Parent!.FullName;

var processes = new List<Process>();
var consoleLock = new Lock();

void Write(string name, ConsoleColor color, string? line)
{
    if (line is null) return;
    lock (consoleLock)
    {
        Console.ForegroundColor = color;
        Console.Write($"[{name,-13}] ");
        Console.ResetColor();
        Console.WriteLine(line);
    }
}

foreach (var (name, project, port, color) in chosen)
{
    var projectFolder = Path.Combine(root, project);
    var dll = Path.Combine(projectFolder, "bin", configuration, tfm, $"{project}.dll");
    if (!File.Exists(dll))
    {
        Console.WriteLine($"Not found: {dll}. Build the solution first.");
        return 1;
    }

    var info = new ProcessStartInfo("dotnet", $"\"{dll}\" --urls http://localhost:{port}")
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
    processes.Add(p);
    Write(name, color, $"started on http://localhost:{port} (pid {p.Id})");
}

Console.WriteLine();
Console.WriteLine("Client entry point: http://localhost:5200 (gateway). Ctrl+C to stop everything.");
Console.WriteLine();

var end = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    end.TrySetResult();
};
await end.Task;

foreach (var p in processes.Where(p => !p.HasExited))
    p.Kill(entireProcessTree: true);
foreach (var p in processes)
    await p.WaitForExitAsync();

return 0;
