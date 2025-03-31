using DockiUp.Application.Commands;
using DockiUp.Application.Dtos.User;
using DockiUp.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DockiUp.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IMediator _mediator;
        public UserController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpGet("GetUsers", Name = "GetUsers")]
        public async Task<ActionResult<UserDto[]>> GetUsers()
        {
            var users = await _mediator.Send(new GetUsersQuery());
            return Ok(users);
        }

        //[HttpGet("GetLoggedInUser", Name = "GetLoggedInUser")]
        //public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
        //{
        //    var users = await _userService.GetAllUsersAsync();
        //    return Ok(users);
        //}

        [HttpGet("GetUser", Name = "GetUser")]
        public async Task<ActionResult<UserDto>> GetUser(int id)
        {
            var user = await _mediator.Send(new GetUserQuery(id));
            return Ok(user);
        }

        [HttpPost("CreateUser", Name = "CreateUser")]
        public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserDto createUserDto)
        {
            var user = await _mediator.Send(new CreateUserCommand(createUserDto));
            return Ok(user);
        }

        [HttpPut("UpdateUser", Name = "UpdateUser")]
        public async Task<IActionResult> UpdateUser(UpdateUserDto updateUserDto)
        {
            await _mediator.Send(new UpdateUserCommand(updateUserDto));
            return NoContent();
        }

        [HttpDelete("DeleteUser", Name = "DeleteUser")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            await _mediator.Send(new DeleteUserCommand(id));
            return NoContent();
        }
    }
}
