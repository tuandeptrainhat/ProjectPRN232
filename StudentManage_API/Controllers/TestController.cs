using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManage_API.Models;

namespace StudentManage_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;

        public TestController(StudentManagementDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Generate BCrypt hash for password
        /// </summary>
        [HttpGet("generate-hash/{password}")]
        public IActionResult GenerateHash(string password)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(password);
            return Ok(new
            {
                password = password,
                hash = hash,
                verification = BCrypt.Net.BCrypt.Verify(password, hash)
            });
        }

        /// <summary>
        /// Test password verification
        /// </summary>
        [HttpPost("verify-password")]
        public IActionResult VerifyPassword([FromBody] VerifyRequest request)
        {
            var isValid = BCrypt.Net.BCrypt.Verify(request.Password, request.Hash);
            return Ok(new
            {
                password = request.Password,
                hash = request.Hash,
                isValid = isValid
            });
        }

        /// <summary>
        /// Get admin user info for debugging
        /// </summary>
        [HttpGet("admin-info")]
        public async Task<IActionResult> GetAdminInfo()
        {
            var admin = await _context.Users
                .Where(u => u.Username == "admin")
                .Select(u => new {
                    u.Id,
                    u.Username,
                    u.Email,
                    u.FullName,
                    u.Role,
                    u.IsActive,
                    PasswordHashLength = u.PasswordHash.Length,
                    PasswordHashStart = u.PasswordHash.Substring(0, Math.Min(20, u.PasswordHash.Length)),
                    CreatedDate = u.CreatedDate,
                    UpdatedDate = u.UpdatedDate
                })
                .FirstOrDefaultAsync();

            if (admin == null)
            {
                return NotFound("Admin user not found");
            }

            return Ok(admin);
        }

        /// <summary>
        /// Test login logic step by step
        /// </summary>
        [HttpPost("debug-login")]
        public async Task<IActionResult> DebugLogin([FromBody] LoginDebugRequest request)
        {
            try
            {
                // Step 1: Find user
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == request.Username);

                if (user == null)
                {
                    return Ok(new
                    {
                        step = "user_lookup",
                        success = false,
                        message = "User not found",
                        username = request.Username
                    });
                }

                // Step 2: Check if active
                if (user.IsActive != true)
                {
                    return Ok(new
                    {
                        step = "user_active_check",
                        success = false,
                        message = "User is not active",
                        isActive = user.IsActive
                    });
                }

                // Step 3: Verify password
                var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

                return Ok(new
                {
                    step = "password_verification",
                    success = passwordValid,
                    message = passwordValid ? "Password is valid" : "Password is invalid",
                    user = new
                    {
                        user.Id,
                        user.Username,
                        user.FullName,
                        user.Role,
                        passwordHashLength = user.PasswordHash.Length,
                        passwordHashStart = user.PasswordHash.Substring(0, Math.Min(10, user.PasswordHash.Length))
                    }
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    step = "exception",
                    success = false,
                    message = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        /// <summary>
        /// Update admin password with proper BCrypt hash
        /// </summary>
        [HttpPost("fix-admin-password")]
        public async Task<IActionResult> FixAdminPassword()
        {
            try
            {
                var admin = await _context.Users.FirstOrDefaultAsync(u => u.Username == "admin");
                if (admin == null)
                {
                    return NotFound("Admin user not found");
                }

                // Generate new hash for "123456"
                var newHash = BCrypt.Net.BCrypt.HashPassword("123456");

                // Verify the new hash works
                var verification = BCrypt.Net.BCrypt.Verify("123456", newHash);

                // Update in database
                admin.PasswordHash = newHash;
                admin.UpdatedDate = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Admin password updated successfully",
                    newHash = newHash,
                    verification = verification,
                    updatedDate = admin.UpdatedDate
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error updating password",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Fix password for all users - Set all to "123456"
        /// </summary>
        [HttpPost("fix-all-passwords")]
        public async Task<IActionResult> FixAllPasswords()
        {
            try
            {
                // Get all active users
                var users = await _context.Users.Where(u => u.IsActive == true).ToListAsync();

                if (!users.Any())
                {
                    return NotFound("No active users found");
                }

                // Generate new hash for "123456"
                var newHash = BCrypt.Net.BCrypt.HashPassword("123456");

                // Verify the hash works
                var verification = BCrypt.Net.BCrypt.Verify("123456", newHash);

                if (!verification)
                {
                    return StatusCode(500, new { message = "Generated hash verification failed" });
                }

                // Update all users
                var updatedUsers = new List<object>();
                foreach (var user in users)
                {
                    user.PasswordHash = newHash;
                    user.UpdatedDate = DateTime.UtcNow;

                    updatedUsers.Add(new
                    {
                        user.Id,
                        user.Username,
                        user.FullName,
                        user.Role
                    });
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "All user passwords updated successfully to '123456'",
                    newHash = newHash,
                    verification = verification,
                    totalUpdated = users.Count,
                    updatedUsers = updatedUsers,
                    updatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error updating passwords",
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        /// <summary>
        /// Get all users info for debugging
        /// </summary>
        [HttpGet("all-users")]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                var users = await _context.Users
                    .Select(u => new {
                        u.Id,
                        u.Username,
                        u.Email,
                        u.FullName,
                        u.Role,
                        u.IsActive,
                        PasswordHashLength = u.PasswordHash.Length,
                        PasswordHashStart = u.PasswordHash.Substring(0, Math.Min(15, u.PasswordHash.Length)),
                        CreatedDate = u.CreatedDate,
                        UpdatedDate = u.UpdatedDate
                    })
                    .OrderBy(u => u.Role)
                    .ThenBy(u => u.Username)
                    .ToListAsync();

                return Ok(new
                {
                    totalUsers = users.Count,
                    users = users
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error getting users",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Test specific user login
        /// </summary>
        [HttpPost("test-user-login")]
        public async Task<IActionResult> TestUserLogin([FromBody] TestLoginRequest request)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == request.Username);

                if (user == null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = "User not found",
                        username = request.Username
                    });
                }

                var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

                return Ok(new
                {
                    success = passwordValid,
                    message = passwordValid ? "Login would succeed" : "Login would fail - wrong password",
                    user = new
                    {
                        user.Id,
                        user.Username,
                        user.FullName,
                        user.Role,
                        user.IsActive,
                        passwordHashLength = user.PasswordHash.Length
                    },
                    passwordTest = new
                    {
                        providedPassword = request.Password,
                        hashFromDB = user.PasswordHash.Substring(0, Math.Min(20, user.PasswordHash.Length)) + "...",
                        verificationResult = passwordValid
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error during test login",
                    error = ex.Message
                });
            }
        }
    }

    public class VerifyRequest
    {
        public string Password { get; set; }
        public string Hash { get; set; }
    }

    public class LoginDebugRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class TestLoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}