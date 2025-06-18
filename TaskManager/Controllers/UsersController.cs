using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using TaskManager.DTOs;
using TaskManager.Models;
using TaskManager.Services;

namespace TaskManager.Controllers
{
  [Route("api/[controller]")]
  [ApiController]
  public class UsersController : ControllerBase
  {
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public UsersController(ApplicationDbContext context, IConfiguration configuration)
    {
      _context = context;
      _configuration = configuration;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] User user)
    {
      var existingUser = _context.Users.FirstOrDefault(u => u.Username == user.Username || u.Email == user.Email);
      if (existingUser != null)
      {
        return BadRequest("Username ou e-mail já está em uso.");
      }

      var passwordHasher = new PasswordHasher<User>();
      user.PasswordHash = passwordHasher.HashPassword(user, user.PasswordHash);

      _context.Users.Add(user);
      await _context.SaveChangesAsync();

      return Ok(new { message = "Usuário registrado com sucesso", userId = user.Id });
    }

    /// <summary>
    /// Authenticate a user with their credentials.
    /// </summary>
    /// <remarks>
    /// The login endpoint is protected against brute force attacks. After 5 failed login attempts, access will be blocked temporarily.
    /// </remarks>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] UserLoginDto loginDto, [FromServices] LoginAttemptService loginAttemptService)
    {
      if (loginAttemptService.IsBlocked(loginDto.Username))
      {
        await System.Threading.Tasks.Task.Delay(2000);
        return Forbid("Too many failed attempts. Please try again later.");
      }

      var user = _context.Users.FirstOrDefault(u => u.Username == loginDto.Username && u.PasswordHash == loginDto.Password);
      if (user == null)
      {
        loginAttemptService.RecordFailedAttempt(loginDto.Username);
        return Unauthorized("Usuário ou senha inválidos.");
      }

      var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_configuration["JWT_SECRET"]));
      var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

      var claims = new[]
      {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username)
      };

      var token = new JwtSecurityToken(
          claims: claims,
          expires: DateTime.Now.AddHours(1),
          signingCredentials: creds
      );

      var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

      loginAttemptService.ResetAttempts(loginDto.Username);

      return Ok(new { token = tokenString });
    }
  }
}
