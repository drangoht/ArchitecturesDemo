using Layered.Business;
using Microsoft.AspNetCore.Mvc;

namespace Layered.Presentation.Controllers;

public record WithdrawalRequest(decimal Amount);
public record TransferRequest(Guid Source, Guid Target, decimal Amount);

/// <summary>
/// Classic controller: it receives the request, calls the business layer and translates errors into HTTP.
/// It only sees the layer right below it (Layered.Business).
/// </summary>
[ApiController]
[Route("")]
public class AccountsController(AccountsService accounts) : ControllerBase
{
    [HttpGet("accounts/{id:guid}")]
    public Task<IActionResult> Get(Guid id) =>
        Execute(async () => Ok(await accounts.Get(id)));

    [HttpPost("accounts/{id:guid}/withdrawal")]
    public Task<IActionResult> Withdraw(Guid id, WithdrawalRequest request) =>
        Execute(async () => Ok(await accounts.Withdraw(id, request.Amount)));

    [HttpPost("transfers")]
    public Task<IActionResult> Transfer(TransferRequest request) =>
        Execute(async () =>
        {
            await accounts.Transfer(request.Source, request.Target, request.Amount);
            return NoContent();
        });

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AccountNotFoundException e)
        {
            return NotFound(new { error = e.Message });
        }
        catch (BusinessRuleException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }
}
