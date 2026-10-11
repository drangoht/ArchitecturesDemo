using Styles.Banque;
using Styles.Outillage.Grpc;
using P = Styles.Contrats;

namespace Styles.Serveur;

/// <summary>
/// L'API gRPC : des PROCÉDURES (des verbes : ObtenirTableauDeBord, EffectuerVirement…), décrites dans banque.proto, et
/// des messages binaires compacts. Le contrat est strict et partagé : client et serveur sont générés à partir du même
/// fichier. Chaque méthode est taillée pour un usage (l'écran d'accueil en un appel), et le serveur peut POUSSER des
/// messages (flux). Idéal entre services ; un navigateur, lui, ne sait pas l'appeler directement.
/// </summary>
public static class ApiGrpc
{
    public static void MapGrpc(this IEndpointRouteBuilder app)
    {
        var banque = app.ServiceProvider.GetRequiredService<ServiceBancaire>();
        const string service = P.ServiceBanque.Nom;

        app.MapGrpcUnaire<P.DemandeTableauDeBord, P.TableauDeBord>(service, "ObtenirTableauDeBord", (demande, _) => Traduire(() =>
        {
            var client = banque.Client(demande.ClientId);
            var comptes = banque.ComptesDe(client.Id);
            var nombre = demande.OperationsParCompte == 0 ? 3 : demande.OperationsParCompte;   // 0 = non renseigné (proto3)
            var operations = banque.DernieresOperations(comptes.Select(c => c.Numero).ToList(), nombre);   // une seule lecture
            var tableau = new P.TableauDeBord { ClientId = client.Id, Nom = client.Nom };
            tableau.Comptes.AddRange(comptes.Select(c => Message(c, operations[c.Numero])));
            return tableau;
        }));

        app.MapGrpcUnaire<P.DemandeCompte, P.Compte>(service, "ObtenirCompte", (demande, _) =>
            Traduire(() => Message(banque.Compte(demande.Numero), [])));

        app.MapGrpcUnaire<P.DemandeVirement, P.Virement>(service, "EffectuerVirement", (demande, _) => Traduire(() =>
        {
            var v = banque.EffectuerVirement(demande.Source, demande.Cible, demande.MontantCentimes / 100m, demande.Libelle);
            return new P.Virement
            {
                Id = v.Id, Source = v.Source, Cible = v.Cible,
                MontantCentimes = Centimes(v.Montant), SoldeSourceCentimes = Centimes(v.SoldeSource)
            };
        }));

        // Le flux serveur : la réponse HTTP/2 reste ouverte, et chaque opération y part dès qu'elle a lieu.
        app.MapGrpcFluxServeur<P.DemandeSuivi, P.Operation>(service, "SuivreOperations", async (demande, ecrire, annulation) =>
        {
            var operations = await Traduire(() => banque.Suivre(demande.Numero, annulation));
            await foreach (var operation in operations.ReadAllAsync(annulation))
                await ecrire(Message(operation));
        });
    }

    private static Task<T> Traduire<T>(Func<T> traitement)
    {
        try
        {
            return Task.FromResult(traitement());
        }
        catch (ErreurMetier e)
        {
            throw new ErreurGrpc(Traductions.StatutGrpc(e.Code), e.Message);
        }
    }

    private static long Centimes(decimal euros) => (long)(euros * 100);

    private static P.Compte Message(Compte compte, IEnumerable<Operation> operations)
    {
        var message = new P.Compte
        {
            Numero = compte.Numero,
            Libelle = compte.Libelle,
            Type = compte.Type == TypeCompte.Courant ? P.TypeCompte.Courant : P.TypeCompte.Epargne,
            SoldeCentimes = Centimes(compte.Solde)
        };
        message.DernieresOperations.AddRange(operations.Select(Message));
        return message;
    }

    private static P.Operation Message(Operation operation) => new()
    {
        Id = operation.Id,
        DateUnixMs = operation.Date.ToUnixTimeMilliseconds(),
        Libelle = operation.Libelle,
        MontantCentimes = Centimes(operation.Montant)
    };
}
