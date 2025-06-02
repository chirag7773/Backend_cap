using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using EdySyncProject.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

public class RefreshTokenDTO
{
    public string Token { get; set; }
    public string RefreshToken { get; set; }
}

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly EduSyncContext _context;
    private readonly IConfiguration _configuration;
    private readonly EmailService _emailService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        EduSyncContext context, 
        IConfiguration configuration, 
        EmailService emailService,
        ILogger<AuthController> logger)
    {
        _context = context;
        _configuration = configuration;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpPost("register")]
    [EnableRateLimiting("fixed")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] CreateUserDTO dto)
    {
        // Validate role
        var role = dto.Role?.Trim().ToLower();
        if (string.IsNullOrWhiteSpace(role) || (role != "student" && role != "instructor"))
            return BadRequest("Role must be either 'Student' or 'Instructor'.");

        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length < 3)
            return BadRequest("Name must be at least 3 characters long.");

        if (string.IsNullOrWhiteSpace(dto.Email) || !Regex.IsMatch(dto.Email, @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$"))
            return BadRequest("Invalid email address.");

        if (string.IsNullOrWhiteSpace(dto.PasswordHash) || dto.PasswordHash.Length < 8)
            return BadRequest("Password must be at least 8 characters long.");

        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            return BadRequest("Email already in use.");

        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.PasswordHash);

        var user = new User
        {
            Name = dto.Name,
            Email = dto.Email,
            Role = char.ToUpper(role[0]) + role.Substring(1), // Capitalize first letter
            PasswordHash = hashedPassword
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        try
        {
            await _emailService.SendWelcomeEmailAsync(dto.Email, dto.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError("Email send failed: " + ex.Message);
        }

        return Ok($"Registration successful as {user.Role}!");
    }


    [HttpPost("login")]
    [EnableRateLimiting("fixed")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginDTO loginDto)
    {
        _logger.LogInformation($"Login attempt for email: {loginDto.Email}");
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == loginDto.Email);
        if (user == null)
        {
            _logger.LogWarning($"User not found: {loginDto.Email}");
            return Unauthorized("Invalid credentials.");
        }

        if (!BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
        {
            _logger.LogWarning($"Invalid password for user: {loginDto.Email}");
            return Unauthorized("Invalid credentials.");
        }

        // Log detailed user information
        _logger.LogInformation($"User {user.Email} authenticated successfully. User details: {System.Text.Json.JsonSerializer.Serialize(new {
            user.UserId,
            user.Email,
            user.Role,
            HasRole = !string.IsNullOrEmpty(user.Role),
            RoleType = user.Role?.GetType().Name
        })}");

        var (token, refreshToken) = GenerateJwtToken(user); 
        
        // Ensure we have a valid role
        var role = !string.IsNullOrEmpty(user.Role) ? user.Role.Trim().ToLowerInvariant() : "student";
        
        var response = new 
        { 
            token, 
            refreshToken,
            role,
            userId = user.UserId,
            name = user.Name
        };
        
        _logger.LogInformation($"Sending login response: {System.Text.Json.JsonSerializer.Serialize(response)}");
        return Ok(response);
    }
    [HttpPost("forgot-password")]
    [EnableRateLimiting("fixed")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDTO dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
        if (user == null)
            return Ok(); 

        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        var resetToken = new PasswordResetToken
        {
            UserId = user.UserId,
            Token = token,
            ExpiresAt = expiresAt,
            Used = false
        };
        _context.PasswordResetTokens.Add(resetToken);
        await _context.SaveChangesAsync();

        var resetLink = $"http://localhost:3000/reset-password?token={Uri.EscapeDataString(token)}";
        try
        {
            await _emailService.SendPasswordResetEmailAsync(user.Email, user.Name, resetLink);
        }
        catch (Exception ex)
        {
            _logger.LogError("Email send failed: " + ex.Message);
        }

        return Ok();
    }
    [HttpPost("reset-password")]
    [EnableRateLimiting("fixed")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDTO dto)
    {
        var tokenEntry = await _context.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t =>
                t.Token == dto.Token &&
                !t.Used &&
                t.ExpiresAt > DateTime.UtcNow);

        if (tokenEntry == null)
            return BadRequest("Invalid or expired reset token.");

        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 8)
            return BadRequest("Password must be at least 8 characters long.");

        tokenEntry.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);

        tokenEntry.Used = true;
        await _context.SaveChangesAsync();

        return Ok("Password has been reset successfully.");
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDTO dto)
    {
        try
        {
            if (string.IsNullOrEmpty(dto.Token) || string.IsNullOrEmpty(dto.RefreshToken))
                return BadRequest("Token and refresh token are required");

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));

            // Validate the refresh token
            var refreshTokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            SecurityToken validatedToken;
            var principal = tokenHandler.ValidateToken(dto.RefreshToken, refreshTokenValidationParameters, out validatedToken);

            // Get the user ID from the refresh token
            var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrEmpty(userId))
                return BadRequest("Invalid refresh token");

            // Get the user from the database
            var user = await _context.Users.FindAsync(Guid.Parse(userId));
            if (user == null)
                return BadRequest("User not found");

            // Generate new tokens
            var (newToken, newRefreshToken) = GenerateJwtToken(user);

            return Ok(new
            {
                token = newToken,
                refreshToken = newRefreshToken,
                role = user.Role,
                userId = user.UserId,
                name = user.Name
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Token refresh failed: {ex.Message}");
            return BadRequest("Invalid refresh token");
        }
    }

    private (string Token, string RefreshToken) GenerateJwtToken(User user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(Convert.ToDouble(_configuration["Jwt:TokenExpirationInMinutes"])),
            signingCredentials: creds
        );

        var refreshToken = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()) },
            expires: DateTime.UtcNow.AddDays(Convert.ToDouble(_configuration["Jwt:RefreshTokenExpirationInDays"])),
            signingCredentials: creds
        );

        var tokenHandler = new JwtSecurityTokenHandler();
        return (
            tokenHandler.WriteToken(token),
            tokenHandler.WriteToken(refreshToken)
        );
    }
}