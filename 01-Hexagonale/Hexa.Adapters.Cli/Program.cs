using System.Globalization;
using System.Text;
using Hexa.Adapters.Notifications;
using Hexa.Adapters.Persistance;
using Hexa.Hexagone.Application;
using Hexa.Hexagone.Domaine;
using Hexa.Hexagone.Ports.Entrants;
using Hexa.Hexagone.Ports.Sortants;

// Adapter entrant « ligne de commande » : même hexagone que l'API web, autre porte d'entrée.
// Composition root manuelle, sans conteneur d'injection : l'hexagone n'en a pas besoin.

Console.OutputEncoding = Encoding.UTF8;

IComptes comptes = new ComptesFichierJson("comptes.json", DonneesDeDemo.Comptes());
INotifications notifications = new NotificationsJournal(Console.Out);

IConsulterCompte consulter = new ConsulterCompte(comptes);
IRetirerArgent retirer = new RetirerArgent(comptes, notifications);
IEffectuerVirement virer = new EffectuerVirement(comptes, notifications);

try
{
    switch (args)
    {
        case ["solde", var id]:
            var vue = await consulter.Executer(Guid.Parse(id));
            Console.WriteLine($"{vue.Titulaire} : {vue.Solde:0.00} €");
            break;

        case ["retirer", var id, var montant]:
            await retirer.Executer(Guid.Parse(id), Montant(montant));
            Console.WriteLine("Retrait effectué.");
            break;

        case ["virer", var source, var cible, var montant]:
            await virer.Executer(Guid.Parse(source), Guid.Parse(cible), Montant(montant));
            Console.WriteLine("Virement effectué.");
            break;

        default:
            Console.WriteLine("""
                Usage :
                  solde   <compteId>
                  retirer <compteId> <montant>
                  virer   <sourceId> <cibleId> <montant>

                Comptes de démo (stockés dans comptes.json, dans le dossier courant) :
                  Alice = 11111111-1111-1111-1111-111111111111
                  Bob   = 22222222-2222-2222-2222-222222222222
                """);
            return 1;
    }
    return 0;
}
catch (Exception e) when (e is RegleMetierException or CompteIntrouvableException)
{
    Console.Error.WriteLine($"Erreur : {e.Message}");
    return 2;
}

static decimal Montant(string texte) => decimal.Parse(texte, CultureInfo.InvariantCulture);
