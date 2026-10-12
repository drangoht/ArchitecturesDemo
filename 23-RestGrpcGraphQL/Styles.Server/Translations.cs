using System.Text.RegularExpressions;
using Styles.Bank;
using Styles.Tooling.Grpc;

namespace Styles.Server;

/// <summary>
/// One business error, three languages. REST has the HTTP statuses (and the "problem+json" body); gRPC has its 17 statuses,
/// carried in the trailers; GraphQL answers 200 and puts the error in "errors", next to the data obtained.
/// </summary>
public static partial class Translations
{
    public static int HttpStatus(ErrorCode code) => code switch
    {
        ErrorCode.UnknownCustomer or ErrorCode.UnknownAccount or ErrorCode.UnknownTransfer => StatusCodes.Status404NotFound,
        ErrorCode.InvalidParameter or ErrorCode.InvalidAmount or ErrorCode.SameAccount => StatusCodes.Status400BadRequest,
        ErrorCode.InsufficientBalance => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError
    };

    public static GrpcStatus GrpcStatus(ErrorCode code) => code switch
    {
        ErrorCode.UnknownCustomer or ErrorCode.UnknownAccount or ErrorCode.UnknownTransfer => Tooling.Grpc.GrpcStatus.NotFound,
        ErrorCode.InvalidParameter or ErrorCode.InvalidAmount or ErrorCode.SameAccount => Tooling.Grpc.GrpcStatus.InvalidArgument,
        ErrorCode.InsufficientBalance => Tooling.Grpc.GrpcStatus.FailedPrecondition,
        _ => Tooling.Grpc.GrpcStatus.Internal
    };

    /// <summary>GraphQL does not standardize the codes: "extensions.code" is free. We reuse the business code: INSUFFICIENT_BALANCE.</summary>
    public static string GraphQLCode(ErrorCode code) => Capitals().Replace(code.ToString(), "_$1").ToUpperInvariant();

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex Capitals();
}
