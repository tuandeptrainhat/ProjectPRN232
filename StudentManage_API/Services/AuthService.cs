using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StudentManage_API.DTOs;
using StudentManage_API.Interfaces;
using StudentManage_API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace StudentManage_API.Services
{
    public class AuthService : IAuthService
    {
        private readonly StudentManagementDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            StudentManagementDbContext context,
            IConfiguration configuration,
            ILogger<AuthService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ApiResponseDto<LoginResponseDto>> LoginAsync(LoginRequestDto request)
        {
            try
            {
                _logger.LogInformation($"Login attempt for username: {request.Username}");

                // Tìm user theo username
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive == true);

                if (user == null)
                {
                    _logger.LogWarning($"User not found: {request.Username}");
                    return ApiResponseDto<LoginResponseDto>.ErrorResult("Invalid username or password");
                }

                // Kiểm tra password (giả sử bạn đang dùng BCrypt)
                if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    _logger.LogWarning($"Invalid password for user: {request.Username}");
                    return ApiResponseDto<LoginResponseDto>.ErrorResult("Invalid username or password");
                }

                // Tạo UserInfo
                var userInfo = new UserInfoDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    FullName = user.FullName,
                    Email = user.Email,
                    Role = user.Role,
                    Phone = user.Phone,
                    Address = user.Address
                };

                // Tạo JWT token
                var token = GenerateJwtToken(userInfo);
                var expires = DateTime.UtcNow.AddMinutes(_configuration.GetValue<int>("Jwt:DurationInMinutes"));

                var loginResponse = new LoginResponseDto
                {
                    Token = token,
                    Expires = expires,
                    User = userInfo
                };

                _logger.LogInformation($"Login successful for user: {request.Username}");
                return ApiResponseDto<LoginResponseDto>.SuccessResult(loginResponse, "Login successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during login for user: {request.Username}");
                return ApiResponseDto<LoginResponseDto>.ErrorResult("An error occurred during login");
            }
        }

        public string GenerateJwtToken(UserInfoDto user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("FullName", user.FullName ?? ""),
                new Claim("UserId", user.Id.ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_configuration.GetValue<int>("Jwt:DurationInMinutes")),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task<UserInfoDto> GetUserInfoAsync(int userId)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive == true);

                if (user == null) return null;

                return new UserInfoDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    FullName = user.FullName,
                    Email = user.Email,
                    Role = user.Role,
                    Phone = user.Phone,
                    Address = user.Address
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user info for userId: {userId}");
                return null;
            }
        }

        public async Task<bool> ValidateUserCredentialsAsync(string username, string password)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == username && u.IsActive == true);

                if (user == null) return false;

                return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating credentials for username: {username}");
                return false;
            }
        }

        public async Task<ApiResponseDto<bool>> ChangePasswordAsync(int userId, ChangePasswordDto request)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null || user.IsActive != true)
                {
                    return ApiResponseDto<bool>.ErrorResult("User not found");
                }

                // Kiểm tra mật khẩu hiện tại
                if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                {
                    return ApiResponseDto<bool>.ErrorResult("Current password is incorrect");
                }

                // Cập nhật mật khẩu mới
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                user.UpdatedDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Password changed successfully for userId: {userId}");
                return ApiResponseDto<bool>.SuccessResult(true, "Password changed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error changing password for userId: {userId}");
                return ApiResponseDto<bool>.ErrorResult("An error occurred while changing password");
            }
        }
    }
}