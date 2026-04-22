using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETmessenger.Application.Abstractions.Push;
using NETmessenger.Contracts.Push;

namespace NETmessenger.Web.Controllers.Push;

[ApiController]
[Route("api/push")]
public class PushController(IPushNotificationService pushNotificationService) : ControllerBase
{
    [HttpGet("public-key")]
    public ActionResult<PushVapidPublicKeyDto> GetPublicKey()
    {
        var result = pushNotificationService.GetPublicKey();
        if (result is null)
        {
            return NotFound(new { error = "Push is not configured on server." });
        }

        return Ok(result);
    }

    [Authorize]
    [HttpPost("subscriptions")]
    public async Task<IActionResult> UpsertSubscription(
        [FromBody] PushSubscriptionRequestDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthorizedUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            await pushNotificationService.UpsertSubscriptionAsync(userId, dto, cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize]
    [HttpGet("status")]
    public async Task<ActionResult<PushSubscriptionStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        if (!TryGetAuthorizedUserId(out var userId))
        {
            return Unauthorized();
        }

        var status = await pushNotificationService.GetStatusAsync(userId, cancellationToken);
        return Ok(status);
    }

    [Authorize]
    [HttpPost("test-self")]
    public async Task<ActionResult<PushTestSelfResultDto>> TestSelf(CancellationToken cancellationToken)
    {
        if (!TryGetAuthorizedUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await pushNotificationService.SendTestPushToUserAsync(userId, cancellationToken);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("debug/subscribe-failure")]
    public async Task<IActionResult> DebugSubscribeFailure(
        [FromBody] PushSubscribeFailureDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthorizedUserId(out var userId))
        {
            return Unauthorized();
        }

        await pushNotificationService.TrackClientSubscribeFailureAsync(userId, dto, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpDelete("subscriptions")]
    public async Task<IActionResult> DeleteSubscription(
        [FromBody] PushSubscriptionDeleteRequestDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthorizedUserId(out var userId))
        {
            return Unauthorized();
        }

        await pushNotificationService.RemoveSubscriptionAsync(userId, dto.Endpoint, cancellationToken);
        return NoContent();
    }

    private bool TryGetAuthorizedUserId(out Guid userId)
    {
        var raw = User.FindFirst("user_id")?.Value;
        return Guid.TryParse(raw, out userId);
    }
}
