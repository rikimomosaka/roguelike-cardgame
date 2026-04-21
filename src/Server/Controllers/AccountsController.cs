using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/accounts")]
public sealed class AccountsController : ControllerBase
{
    private readonly IAccountRepository _accounts;

    public AccountsController(IAccountRepository accounts) => _accounts = accounts;

    public sealed record CreateAccountRequest([Required] string AccountId);

    public sealed record AccountResponse(string Id, DateTimeOffset CreatedAt);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { error = "body required" });

        try
        {
            AccountIdValidator.Validate(request.AccountId);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            await _accounts.CreateAsync(request.AccountId, now, ct);
            return CreatedAtAction(
                nameof(Get),
                new { id = request.AccountId },
                new AccountResponse(request.AccountId, now));
        }
        catch (AccountAlreadyExistsException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        try
        {
            AccountIdValidator.Validate(id);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        var account = await _accounts.GetAsync(id, ct);
        if (account is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {id}");

        return Ok(new AccountResponse(account.Id, account.CreatedAt));
    }
}
