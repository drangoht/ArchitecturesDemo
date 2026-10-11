using System.Text.RegularExpressions;
using Styles.Banque;
using Styles.Outillage.Grpc;

namespace Styles.Serveur;

/// <summary>
/// Une erreur métier, trois langues. REST a les statuts HTTP (et le corps « problem+json ») ; gRPC a ses 17 statuts,
/// transportés dans les trailers ; GraphQL répond 200 et met l'erreur dans « errors », à côté des données obtenues.
/// </summary>
public static partial class Traductions
{
    public static int StatutHttp(CodeErreur code) => code switch
    {
        CodeErreur.ClientInconnu or CodeErreur.CompteInconnu or CodeErreur.VirementInconnu => StatusCodes.Status404NotFound,
        CodeErreur.ParametreInvalide or CodeErreur.MontantInvalide or CodeErreur.MemeCompte => StatusCodes.Status400BadRequest,
        CodeErreur.SoldeInsuffisant => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError
    };

    public static StatutGrpc StatutGrpc(CodeErreur code) => code switch
    {
        CodeErreur.ClientInconnu or CodeErreur.CompteInconnu or CodeErreur.VirementInconnu => Outillage.Grpc.StatutGrpc.NotFound,
        CodeErreur.ParametreInvalide or CodeErreur.MontantInvalide or CodeErreur.MemeCompte => Outillage.Grpc.StatutGrpc.InvalidArgument,
        CodeErreur.SoldeInsuffisant => Outillage.Grpc.StatutGrpc.FailedPrecondition,
        _ => Outillage.Grpc.StatutGrpc.Internal
    };

    /// <summary>GraphQL ne normalise pas les codes : « extensions.code » est libre. On reprend le code métier : SOLDE_INSUFFISANT.</summary>
    public static string CodeGraphQL(CodeErreur code) => Majuscules().Replace(code.ToString(), "_$1").ToUpperInvariant();

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex Majuscules();
}
