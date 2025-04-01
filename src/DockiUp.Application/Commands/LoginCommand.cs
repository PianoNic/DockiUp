using DockiUp.Application.Dtos.User;
using DockiUp.Domain.Models;
using DockiUp.Infrastructure;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DockiUp.Application.Commands
{
    public class LoginCommand : IRequest<string>
    {
        public LoginCommand(UserLoginDto loginData)
        {
            LoginData = loginData;
        }

        public UserLoginDto LoginData { get; set; }
    }

    public class GenerateJwtTokenCommandHandler : IRequestHandler<LoginCommand, string>
    {
        private readonly IConfiguration _configuration;
        private readonly DockiUpDbContext _DbContext;

        public GenerateJwtTokenCommandHandler(IConfiguration configuration, DockiUpDbContext dockiUpDbContext)
        {
            _configuration = configuration;
            _DbContext = dockiUpDbContext;
        }

        public async Task<string> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            var user = _DbContext.Users.Single(u => u.Username == request.LoginData.Username || u.Email == request.LoginData.Username);
            var passwordIsValid = BCrypt.Net.BCrypt.Verify(request.LoginData.Password, user.PasswordHash);

            if (user == null || passwordIsValid == false)
                throw new UnauthorizedAccessException("Invalid username, email or password");

            var jwt = await HandleJwtCreation(user);
            return jwt;
        }

        private Task<string> HandleJwtCreation(User user)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Name, user.Username),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JWT_SECRET_KEY"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddHours(1),
                signingCredentials: creds);

            return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
        }
    }
}
