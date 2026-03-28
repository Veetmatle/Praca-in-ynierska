using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StudentApp.Api.Data.Entities;
using StudentApp.Api.Models.Requests;
using StudentApp.Api.Services;

namespace StudentApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("chat")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService) => _chatService = chatService;

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] string? category)
    {
        var userId = GetUserId();
        ChatCategory? cat = null;
        if (!string.IsNullOrEmpty(category) && Enum.TryParse<ChatCategory>(category, true, out var parsed))
            cat = parsed;

        var sessions = await _chatService.GetUserSessionsAsync(userId, cat);
        return Ok(sessions);
    }

    [HttpGet("sessions/{publicId:guid}")]
    public async Task<IActionResult> GetSession(Guid publicId)
    {
        var detail = await _chatService.GetSessionDetailAsync(publicId, GetUserId());
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest request)
    {
        try
        {
            var session = await _chatService.CreateSessionAsync(GetUserId(), request);
            return CreatedAtAction(nameof(GetSession), new { publicId = session.PublicId }, session);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("sessions/{publicId:guid}")]
    public async Task<IActionResult> DeleteSession(Guid publicId)
    {
        var success = await _chatService.DeleteSessionAsync(publicId, GetUserId());
        return success ? NoContent() : NotFound();
    }

    [HttpPost("sessions/{publicId:guid}/pin")]
    public async Task<IActionResult> TogglePin(Guid publicId)
    {
        var userId = GetUserId();
        var session = await _chatService.TogglePinAsync(publicId, userId);
        if (session is null) return NotFound();
        return Ok(session);
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.Parse(claim!);
    }
}
