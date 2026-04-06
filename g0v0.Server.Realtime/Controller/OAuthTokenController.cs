// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Database.Models;
using g0v0.Server.Common.Database.Repository;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace g0v0.Server.Realtime.Controller;

[ApiController]
[Route("[controller]")]
public class OAuthTokenController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "ClientOnly")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OAuthToken>> GetOAuthToken([FromServices] IOAuthTokenRepository repository)
    {
        var accessToken = await HttpContext.GetTokenAsync("access_token");

        if (string.IsNullOrEmpty(accessToken))
        {
            return Unauthorized();
        }

        var token = await repository.GetByAccessTokenAsync(accessToken);

        return token ?? (ActionResult<OAuthToken>)Unauthorized();
    }
}