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

        [HttpGet("CheckGitLink", Name = "CheckGitLink")]
        [ProducesResponseType(200)]
        public async Task<ActionResult<int>> CheckGitLink(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return BadRequest("URL cannot be null or empty.");
            }

            try
            {
                var response = await _httpClient.GetAsync(url);
                return Ok((int)response.StatusCode);
            }
            catch (HttpRequestException)
            {
                return Ok(500);
            }
        }
    }
}
