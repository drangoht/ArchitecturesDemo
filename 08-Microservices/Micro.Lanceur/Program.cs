using System.Diagnostics;
using System.Text;

// Usage : dotnet run --project Micro.Lanceur [-- service1 service2 …]
// Sans argument, tous les services démarrent. Avec des noms, seulement ceux-là
// (pratique pour arrêter un service et observer comment les autres se comportent).

Console.OutputEncoding = Encoding.UTF8;

(string Nom, string Projet, int Port, ConsoleColor Couleur)[] services =
[
    ("courtier", "Micro.Courtier", 5209, ConsoleColor.DarkGray),
    ("comptes", "Micro.Comptes", 5201, ConsoleColor.Cyan),
    ("virements", "Micro.Virements", 5202, ConsoleColor.Yellow),
    ("notifications", "Micro.Notifications", 5203, ConsoleColor.Magenta),
    ("passerelle", "Micro.Passerelle", 5200, ConsoleColor.Green)
];

var choisis = args.Length == 0 ? services : services.Where(s => args.Contains(s.Nom)).ToArray();
if (choisis.Length == 0)
{
    Console.WriteLine($"Services connus : {string.Join(", ", services.Select(s => s.Nom))}");
    return 1;
}

// bin/<Configuration>/<tfm>/ du lanceur → mêmes configuration et tfm pour les services.
var dossierBinaire = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
var tfm = dossierBinaire.Name;
var configuration = dossierBinaire.Parent!.Name;
var racine = dossierBinaire.Parent.Parent!.Parent!.Parent!.FullName;

var processus = new List<Process>();
var verrouConsole = new Lock();

void Ecrire(string nom, ConsoleColor couleur, string? ligne)
{
    if (ligne is null) return;
    lock (verrouConsole)
    {
        Console.ForegroundColor = couleur;
        Console.Write($"[{nom,-13}] ");
        Console.ResetColor();
        Console.WriteLine(ligne);
    }
}

foreach (var (nom, projet, port, couleur) in choisis)
{
    var dossierProjet = Path.Combine(racine, projet);
    var dll = Path.Combine(dossierProjet, "bin", configuration, tfm, $"{projet}.dll");
    if (!File.Exists(dll))
    {
        Console.WriteLine($"Introuvable : {dll}. Compilez d'abord la solution.");
        return 1;
    }

    var info = new ProcessStartInfo("dotnet", $"\"{dll}\" --urls http://localhost:{port}")
    {
        WorkingDirectory = dossierProjet,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    info.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "false";

    var p = new Process { StartInfo = info, EnableRaisingEvents = true };
    p.OutputDataReceived += (_, e) => Ecrire(nom, couleur, e.Data);
    p.ErrorDataReceived += (_, e) => Ecrire(nom, couleur, e.Data);
    p.Exited += (_, _) => Ecrire(nom, couleur, "processus arrêté.");
    p.Start();
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    processus.Add(p);
    Ecrire(nom, couleur, $"démarré sur http://localhost:{port} (pid {p.Id})");
}

Console.WriteLine();
Console.WriteLine("Point d'entrée des clients : http://localhost:5200 (passerelle). Ctrl+C pour tout arrêter.");
Console.WriteLine();

var fin = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    fin.TrySetResult();
};
await fin.Task;

foreach (var p in processus.Where(p => !p.HasExited))
    p.Kill(entireProcessTree: true);
foreach (var p in processus)
    await p.WaitForExitAsync();

return 0;
