using System.Diagnostics;
using System.Text;

// Usage : dotnet run --project Cell.Lanceur [-- nom1 nom2 …]
// Sans argument : le routeur et les cellules c1, c2, c3. La cellule c4 est « en réserve » : la lancer à part
// (dotnet run --project Cell.Lanceur -- c4), puis l'enregistrer auprès du plan de contrôle (POST /plateforme/cellules).

Console.OutputEncoding = Encoding.UTF8;

(string Nom, string Projet, int Port, string Arguments, ConsoleColor Couleur)[] processus =
[
    ("c1", "Cell.Cellule", 5231, "--Cellule:Id=c1", ConsoleColor.Cyan),
    ("c2", "Cell.Cellule", 5232, "--Cellule:Id=c2", ConsoleColor.Yellow),
    ("c3", "Cell.Cellule", 5233, "--Cellule:Id=c3", ConsoleColor.Magenta),
    ("c4", "Cell.Cellule", 5234, "--Cellule:Id=c4", ConsoleColor.Blue),
    ("routeur", "Cell.Routeur", 5117, "", ConsoleColor.Green)
];

var choisis = args.Length == 0 ? processus.Where(p => p.Nom != "c4").ToArray() : processus.Where(p => args.Contains(p.Nom)).ToArray();
if (choisis.Length == 0)
{
    Console.WriteLine($"Processus connus : {string.Join(", ", processus.Select(p => p.Nom))}");
    return 1;
}

// bin/<Configuration>/<tfm>/ du lanceur → mêmes configuration et tfm pour les autres projets.
var dossierBinaire = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
var tfm = dossierBinaire.Name;
var configuration = dossierBinaire.Parent!.Name;
var racine = dossierBinaire.Parent.Parent!.Parent!.Parent!.FullName;

var lances = new List<Process>();
var verrouConsole = new Lock();

void Ecrire(string nom, ConsoleColor couleur, string? ligne)
{
    if (ligne is null) return;
    lock (verrouConsole)
    {
        Console.ForegroundColor = couleur;
        Console.Write($"[{nom,-8}] ");
        Console.ResetColor();
        Console.WriteLine(ligne);
    }
}

foreach (var (nom, projet, port, arguments, couleur) in choisis)
{
    var dossierProjet = Path.Combine(racine, projet);
    var dll = Path.Combine(dossierProjet, "bin", configuration, tfm, $"{projet}.dll");
    if (!File.Exists(dll))
    {
        Console.WriteLine($"Introuvable : {dll}. Compilez d'abord la solution.");
        return 1;
    }

    var info = new ProcessStartInfo("dotnet", $"\"{dll}\" --urls http://localhost:{port} {arguments}")
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
    lances.Add(p);
    Ecrire(nom, couleur, $"démarré sur http://localhost:{port} (pid {p.Id})");
}

Console.WriteLine();
Console.WriteLine("Point d'entrée des clients : http://localhost:5117 (routeur). Ctrl+C pour tout arrêter.");
Console.WriteLine();

var fin = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    fin.TrySetResult();
};
await fin.Task;

foreach (var p in lances.Where(p => !p.HasExited))
    p.Kill(entireProcessTree: true);
foreach (var p in lances)
    await p.WaitForExitAsync();

return 0;
