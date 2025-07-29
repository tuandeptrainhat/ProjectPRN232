using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManage_API.DTOs;
using StudentManage_API.Models;

namespace StudentManage_API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminUserController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<AdminUserController> _logger;

        public AdminUserController(StudentManagementDbContext context, ILogger<AdminUserController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get all users with filtering by role
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetUsers([FromQuery] string? role = null)
        {
            try
            {
                var query = _context.Users.AsQueryable();

                if (!string.IsNullOrEmpty(role))
                {
                    query = query.Where(u => u.Role == role);
                }

                var users = await query
                    .Where(u => u.IsActive == true)
                    .Select(u => new UserResponseDto
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Email = u.Email,
                        FullName = u.FullName,
                        Role = u.Role,
                        Phone = u.Phone,
                        Address = u.Address,
                        IsActive = u.IsActive ?? false,
                        CreatedDate = u.CreatedDate ?? DateTime.UtcNow
                    })
                    .OrderBy(u => u.Role)
                    .ThenBy(u => u.FullName)
                    .ToListAsync();

                return Ok(ApiResponseDto<List<UserResponseDto>>.SuccessResult(users));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get user by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUser(int id)
        {
            try
            {
                var user = await _context.Users
                    .Where(u => u.Id == id && u.IsActive == true)
                    .Select(u => new UserResponseDto
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Email = u.Email,
                        FullName = u.FullName,
                        Role = u.Role,
                        Phone = u.Phone,
                        Address = u.Address,
                        IsActive = u.IsActive ?? false,
                        CreatedDate = u.CreatedDate ?? DateTime.UtcNow
                    })
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("User not found"));
                }

                return Ok(ApiResponseDto<UserResponseDto>.SuccessResult(user));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Create new user
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList();
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Validation failed", errors));
                }

                // Check if username exists
                if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Username already exists"));
                }

                // Check if email exists
                if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Email already exists"));
                }

                var user = new User
                {
                    Username = dto.Username,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                    Email = dto.Email,
                    FullName = dto.FullName,
                    Role = dto.Role,
                    Phone = dto.Phone,
                    Address = dto.Address,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                var response = new UserResponseDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    FullName = user.FullName,
                    Role = user.Role,
                    Phone = user.Phone,
                    Address = user.Address,
                    IsActive = user.IsActive ?? false,
                    CreatedDate = user.CreatedDate ?? DateTime.UtcNow
                };

                return CreatedAtAction(nameof(GetUser), new { id = user.Id },
                    ApiResponseDto<UserResponseDto>.SuccessResult(response, "User created successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Update user
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList();
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Validation failed", errors));
                }

                var user = await _context.Users.FindAsync(id);
                if (user == null || user.IsActive != true)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("User not found"));
                }

                // Check email uniqueness if changed
                if (!string.IsNullOrEmpty(dto.Email) && dto.Email != user.Email)
                {
                    if (await _context.Users.AnyAsync(u => u.Email == dto.Email && u.Id != id))
                    {
                        return BadRequest(ApiResponseDto<object>.ErrorResult("Email already exists"));
                    }
                    user.Email = dto.Email;
                }

                // Update fields
                if (!string.IsNullOrEmpty(dto.FullName)) user.FullName = dto.FullName;
                if (!string.IsNullOrEmpty(dto.Phone)) user.Phone = dto.Phone;
                if (!string.IsNullOrEmpty(dto.Address)) user.Address = dto.Address;
                if (dto.IsActive.HasValue) user.IsActive = dto.IsActive.Value;

                user.UpdatedDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                var response = new UserResponseDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    FullName = user.FullName,
                    Role = user.Role,
                    Phone = user.Phone,
                    Address = user.Address,
                    IsActive = user.IsActive ?? false,
                    CreatedDate = user.CreatedDate ?? DateTime.UtcNow
                };

                return Ok(ApiResponseDto<UserResponseDto>.SuccessResult(response, "User updated successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Delete user (soft delete)
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("User not found"));
                }

                // Soft delete
                user.IsActive = false;
                user.UpdatedDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "User deleted successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting user {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Reset user password
        /// </summary>
        [HttpPost("{id}/reset-password")]
        public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList();
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Validation failed", errors));
                }

                var user = await _context.Users.FindAsync(id);
                if (user == null || user.IsActive != true)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("User not found"));
                }

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
                user.UpdatedDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Password reset successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resetting password for user {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get users statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<IActionResult> GetUsersStatistics()
        {
            try
            {
                var stats = await _context.Users
                    .Where(u => u.IsActive == true)
                    .GroupBy(u => u.Role)
                    .Select(g => new { Role = g.Key, Count = g.Count() })
                    .ToListAsync();

                var totalUsers = await _context.Users.CountAsync(u => u.IsActive == true);

                var result = new
                {
                    TotalUsers = totalUsers,
                    ByRole = stats
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user statistics");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }
    }

    // DTOs for User Management
    public class CreateUserDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; } // Admin, Teacher, Student
        public string? Phone { get; set; }
        public string? Address { get; set; }
    }

    public class UpdateUserDto
    {
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public bool? IsActive { get; set; }
    }

    public class UserResponseDto
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class ResetPasswordDto
    {
        public string NewPassword { get; set; }
    }
}