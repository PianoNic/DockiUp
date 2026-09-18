using DockiUp.Application.Dtos;
using DockiUp.Application.Queries;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Toamaisutaa.AspNetCore;

namespace DockiUp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly IToamaisutaaClientConfigurationProvider? _authConfig;

        // The provider is only registered when Oidc:Authority is set, so null means open mode.
        public AppController(IMediator mediator, IToamaisutaaClientConfigurationProvider? authConfig = null)
        {
            _mediator = mediator;
            _authConfig = authConfig;
        }

        // Anonymous: the SPA reads this before login to learn where the IdP is and how to configure OIDC.
        [AllowAnonymous]
        [HttpGet("GetAppInfo", Name = "GetAppInfo")]
        [ProducesResponseType(typeof(AppInfoDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<AppInfoDto>> GetAppInfo()
        {
            var appInfo = await _mediator.Send(new GetAppInfoQuery(), HttpContext.RequestAborted);

            if (_authConfig is not null)
            {
                // Redirect URIs fall back to Oidc:PublicUrl, then the request origin (Toamaisutaa's rule).
                var oidc = _authConfig.GetConfiguration(HttpContext);
                appInfo.AuthEnabled = true;
                appInfo.Authority = oidc.Authority;
                appInfo.ClientId = oidc.ClientId;
                appInfo.RedirectUri = oidc.RedirectUri;
                appInfo.PostLogoutRedirectUri = oidc.PostLogoutRedirectUri;
                appInfo.Scope = oidc.Scope;
            }

            return Ok(appInfo);
        }
    }
}
