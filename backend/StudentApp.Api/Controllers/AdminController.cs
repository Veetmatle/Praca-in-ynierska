using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentApp.Api.Models.Requests;
using StudentApp.Api.Models.Responses;
using StudentApp.Api.Services;

namespace StudentApp.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IUserService _userService;

    public AdminController(IUserService userService) => _userService = userService;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeDeleted = false)
    {
        var users = await _userService.GetAllUsersAsync(includeDeleted);
        return Ok(users);
    }

    [HttpGet("{publicId:guid}")]
    public async Task<IActionResult> GetUser(Guid publicId)
    {
        var user = await _userService.GetByPublicIdAsync(publicId);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            var user = await _userService.CreateUserAsync(request);
            return CreatedAtAction(nameof(GetUser), new { publicId = user.PublicId }, user);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiError(ex.Message));
        }
    }

    [HttpDelete("{publicId:guid}")]
    public async Task<IActionResult> DeleteUser(Guid publicId)
    {
        var success = await _userService.SoftDeleteAsync(publicId);
        return success ? NoContent() : NotFound();
    }

    [HttpPost("{publicId:guid}/restore")]
    public async Task<IActionResult> RestoreUser(Guid publicId)
    {
        var success = await _userService.RestoreAsync(publicId);
        return success ? NoContent() : NotFound();
    }
}
