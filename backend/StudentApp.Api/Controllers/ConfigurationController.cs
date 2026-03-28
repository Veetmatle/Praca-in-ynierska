using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StudentApp.Api.Models.Requests;
using StudentApp.Api.Services;

namespace StudentApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("chat")]
public class ConfigurationController : ControllerBase
{
    private readonly IConfigurationService _configService;

    public ConfigurationController(IConfigurationService configService)
        => _configService = configService;

    [HttpGet]
    public async Task<IActionResult> GetConfig()
    {
        var config = await _configService.GetConfigAsync(GetUserId());
        return config is null ? NotFound() : Ok(config);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdateConfigurationRequest request)
    {
        var updated = await _configService.UpdateConfigAsync(GetUserId(), request);
        return Ok(updated);
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.Parse(claim!);
    }
}
