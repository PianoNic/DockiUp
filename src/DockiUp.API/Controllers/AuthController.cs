using DockiUp.Application.Commands;
using DockiUp.Application.Dtos.User;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DockiUp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController : ControllerBase
    {
        private readonly IMediator _mediator;

        public AuthenticationController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpPost("Login", Name = "Login")]
        public async Task<ActionResult<string>> Login([FromBody] UserLoginDto user)
        {
            var token = await _mediator.Send(new LoginCommand(user));
            return Ok(token);
        }
    }
}
