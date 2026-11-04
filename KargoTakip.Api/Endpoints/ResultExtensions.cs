using KargoTakip.Api.Services;

namespace KargoTakip.Api.Endpoints;

public static class ResultExtensions
{
    /// <summary>Turns a refused service call into the matching HTTP problem response.</summary>
    public static IResult ToProblem<T>(this ServiceResult<T> result) => result.Kind switch
    {
        ResultKind.Invalid => Results.ValidationProblem(result.Errors!),
        ResultKind.NotFound => Results.Problem(title: "Kayıt bulunamadı", statusCode: StatusCodes.Status404NotFound),
        ResultKind.Conflict => Results.Problem(title: "İşlem yapılamadı", detail: result.Message, statusCode: StatusCodes.Status409Conflict),
        ResultKind.Forbidden => Results.Problem(title: "Yetkisiz işlem", detail: result.Message ?? "Bu işlem için yetkin yok.", statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };
}
