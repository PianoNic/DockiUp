using DockiUp.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DockiUp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CheckController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly HttpClient _httpClient;

        public CheckController(IMediator mediator, HttpClient httpClient)
        {
            _mediator = mediator;
            _httpClient = httpClient;
        }

        [HttpGet("IsValidGitRepository", Name = "IsValidGitRepository")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<bool>> IsValidGitRepository(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return BadRequest("URL is required.");

            var result = await _mediator.Send(new IsValidGitRepositoryQuery(url));
            return Ok(result);
        }
    }
}
