using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManage_API.DTOs;
using StudentManage_API.Models;
using System.Security.Claims;

namespace StudentManage_API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminNotificationController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<AdminNotificationController> _logger;

        public AdminNotificationController(StudentManagementDbContext context, ILogger<AdminNotificationController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get all notifications with filtering
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetNotifications(
            [FromQuery] string? type = null,
            [FromQuery] string? targetRole = null,
            [FromQuery] bool includeExpired = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var query = _context.Notifications
                    .Include(n => n.CreatedByNavigation)
                    .Include(n => n.Class)
                    .Include(n => n.User)
                    .AsQueryable();

                // Apply filters
                if (!string.IsNullOrEmpty(type))
                {
                    query = query.Where(n => n.Type == type);
                }

                if (!string.IsNullOrEmpty(targetRole))
                {
                    query = query.Where(n => n.TargetRole == targetRole || n.TargetRole == "All");
                }

                if (!includeExpired)
                {
                    query = query.Where(n => n.IsActive == true &&
                                           (n.ExpiryDate == null || n.ExpiryDate > DateTime.UtcNow));
                }

                var totalCount = await query.CountAsync();

                var notifications = await query
                    .OrderByDescending(n => n.CreatedDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(n => new NotificationResponseDto
                    {
                        Id = n.Id,
                        Title = n.Title,
                        Content = n.Content,
                        Type = n.Type,
                        TargetRole = n.TargetRole,
                        ClassId = n.ClassId,
                        ClassName = n.Class != null ? n.Class.ClassName : null,
                        UserId = n.UserId,
                        UserName = n.User != null ? n.User.FullName : null,
                        Priority = n.Priority,
                        CreatedBy = n.CreatedBy,
                        CreatedByName = n.CreatedByNavigation.FullName,
                        CreatedDate = n.CreatedDate ?? DateTime.UtcNow,
                        ExpiryDate = n.ExpiryDate,
                        IsActive = n.IsActive ?? false
                    })
                    .ToListAsync();

                var result = new PaginatedResponseDto<NotificationResponseDto>
                {
                    Data = notifications,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                };

                return Ok(ApiResponseDto<PaginatedResponseDto<NotificationResponseDto>>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notifications");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get notification by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetNotification(int id)
        {
            try
            {
                var notification = await _context.Notifications
                    .Include(n => n.CreatedByNavigation)
                    .Include(n => n.Class)
                    .Include(n => n.User)
                    .Where(n => n.Id == id)
                    .Select(n => new NotificationDetailDto
                    {
                        Id = n.Id,
                        Title = n.Title,
                        Content = n.Content,
                        Type = n.Type,
                        TargetRole = n.TargetRole,
                        ClassId = n.ClassId,
                        ClassName = n.Class != null ? n.Class.ClassName : null,
                        UserId = n.UserId,
                        UserName = n.User != null ? n.User.FullName : null,
                        Priority = n.Priority,
                        CreatedBy = n.CreatedBy,
                        CreatedByName = n.CreatedByNavigation.FullName,
                        CreatedDate = n.CreatedDate ?? DateTime.UtcNow,
                        ExpiryDate = n.ExpiryDate,
                        IsActive = n.IsActive ?? false,
                        // Count recipients based on target
                        EstimatedRecipients = n.TargetRole == "All" ?
                            _context.Users.Count(u => u.IsActive == true) :
                            n.TargetRole != null ?
                            _context.Users.Count(u => u.Role == n.TargetRole && u.IsActive == true) :
                            n.ClassId != null ?
                            _context.StudentClasses.Count(sc => sc.ClassId == n.ClassId && sc.IsActive == true) :
                            n.UserId != null ? 1 : 0
                    })
                    .FirstOrDefaultAsync();

                if (notification == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Notification not found"));
                }

                return Ok(ApiResponseDto<NotificationDetailDto>.SuccessResult(notification));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting notification {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Create new notification
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateNotification([FromBody] CreateNotificationDto dto)
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

                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Validate class if specified
                if (dto.ClassId.HasValue)
                {
                    var classExists = await _context.Classes.AnyAsync(c => c.Id == dto.ClassId && c.IsActive == true);
                    if (!classExists)
                    {
                        return BadRequest(ApiResponseDto<object>.ErrorResult("Invalid class specified"));
                    }
                }

                // Validate user if specified
                if (dto.UserId.HasValue)
                {
                    var userExists = await _context.Users.AnyAsync(u => u.Id == dto.UserId && u.IsActive == true);
                    if (!userExists)
                    {
                        return BadRequest(ApiResponseDto<object>.ErrorResult("Invalid user specified"));
                    }
                }

                var notification = new Notification
                {
                    Title = dto.Title,
                    Content = dto.Content,
                    Type = dto.Type,
                    TargetRole = dto.TargetRole,
                    ClassId = dto.ClassId,
                    UserId = dto.UserId,
                    Priority = dto.Priority ?? "Normal",
                    ExpiryDate = dto.ExpiryDate,
                    CreatedBy = currentUserId,
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true
                };

                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                var response = new NotificationResponseDto
                {
                    Id = notification.Id,
                    Title = notification.Title,
                    Content = notification.Content,
                    Type = notification.Type,
                    TargetRole = notification.TargetRole,
                    ClassId = notification.ClassId,
                    UserId = notification.UserId,
                    Priority = notification.Priority,
                    CreatedBy = notification.CreatedBy,
                    CreatedDate = notification.CreatedDate ?? DateTime.UtcNow,
                    ExpiryDate = notification.ExpiryDate,
                    IsActive = notification.IsActive ?? false
                };

                return CreatedAtAction(nameof(GetNotification), new { id = notification.Id },
                    ApiResponseDto<NotificationResponseDto>.SuccessResult(response, "Notification created successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Update notification
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateNotification(int id, [FromBody] UpdateNotificationDto dto)
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

                var notification = await _context.Notifications.FindAsync(id);
                if (notification == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Notification not found"));
                }

                // Update fields
                if (!string.IsNullOrEmpty(dto.Title)) notification.Title = dto.Title;
                if (!string.IsNullOrEmpty(dto.Content)) notification.Content = dto.Content;
                if (!string.IsNullOrEmpty(dto.Priority)) notification.Priority = dto.Priority;
                if (dto.ExpiryDate.HasValue) notification.ExpiryDate = dto.ExpiryDate;
                if (dto.IsActive.HasValue) notification.IsActive = dto.IsActive;

                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Notification updated successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating notification {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Delete notification (soft delete)
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteNotification(int id)
        {
            try
            {
                var notification = await _context.Notifications.FindAsync(id);
                if (notification == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Notification not found"));
                }

                notification.IsActive = false;
                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Notification deleted successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting notification {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Send notification to all users in target group
        /// </summary>
        [HttpPost("{id}/send")]
        public async Task<IActionResult> SendNotification(int id)
        {
            try
            {
                var notification = await _context.Notifications
                    .Include(n => n.Class)
                    .FirstOrDefaultAsync(n => n.Id == id && n.IsActive == true);

                if (notification == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Notification not found"));
                }

                // Calculate recipients
                var recipientCount = 0;

                if (notification.TargetRole == "All")
                {
                    recipientCount = await _context.Users.CountAsync(u => u.IsActive == true);
                }
                else if (!string.IsNullOrEmpty(notification.TargetRole))
                {
                    recipientCount = await _context.Users.CountAsync(u => u.Role == notification.TargetRole && u.IsActive == true);
                }
                else if (notification.ClassId.HasValue)
                {
                    recipientCount = await _context.StudentClasses.CountAsync(sc => sc.ClassId == notification.ClassId && sc.IsActive == true);
                }
                else if (notification.UserId.HasValue)
                {
                    recipientCount = 1;
                }

                // In a real application, you would implement actual notification sending here
                // (email, push notifications, in-app notifications, etc.)

                var result = new
                {
                    NotificationId = id,
                    Title = notification.Title,
                    EstimatedRecipients = recipientCount,
                    SentAt = DateTime.UtcNow,
                    Status = "Sent"
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result, $"Notification sent to {recipientCount} recipients"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending notification {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get notification statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<IActionResult> GetNotificationStatistics()
        {
            try
            {
                var totalNotifications = await _context.Notifications.CountAsync(n => n.IsActive == true);

                var byType = await _context.Notifications
                    .Where(n => n.IsActive == true)
                    .GroupBy(n => n.Type)
                    .Select(g => new { Type = g.Key, Count = g.Count() })
                    .ToListAsync();

                var byPriority = await _context.Notifications
                    .Where(n => n.IsActive == true)
                    .GroupBy(n => n.Priority)
                    .Select(g => new { Priority = g.Key, Count = g.Count() })
                    .ToListAsync();

                var byTargetRole = await _context.Notifications
                    .Where(n => n.IsActive == true)
                    .GroupBy(n => n.TargetRole)
                    .Select(g => new { TargetRole = g.Key ?? "Specific", Count = g.Count() })
                    .ToListAsync();

                var recentNotifications = await _context.Notifications
                    .Where(n => n.IsActive == true && n.CreatedDate >= DateTime.UtcNow.AddDays(-7))
                    .CountAsync();

                var expiredNotifications = await _context.Notifications
                    .Where(n => n.IsActive == true && n.ExpiryDate < DateTime.UtcNow)
                    .CountAsync();

                var result = new
                {
                    TotalNotifications = totalNotifications,
                    RecentNotifications = recentNotifications,
                    ExpiredNotifications = expiredNotifications,
                    ByType = byType,
                    ByPriority = byPriority,
                    ByTargetRole = byTargetRole
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notification statistics");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get notification templates
        /// </summary>
        [HttpGet("templates")]
        public IActionResult GetNotificationTemplates()
        {
            try
            {
                var templates = new List<NotificationTemplateDto>
                {
                    new NotificationTemplateDto
                    {
                        Name = "Welcome New Student",
                        Type = "Personal",
                        TargetRole = "Student",
                        Priority = "Normal",
                        Title = "Welcome to {SchoolName}",
                        Content = "Dear {StudentName}, welcome to our school! We're excited to have you join us for the {SchoolYear} academic year."
                    },
                    new NotificationTemplateDto
                    {
                        Name = "Class Schedule Update",
                        Type = "Class",
                        TargetRole = "Student",
                        Priority = "High",
                        Title = "Schedule Update for {ClassName}",
                        Content = "There has been an update to your class schedule. Please check your updated timetable for {ClassName}."
                    },
                    new NotificationTemplateDto
                    {
                        Name = "Exam Reminder",
                        Type = "General",
                        TargetRole = "Student",
                        Priority = "High",
                        Title = "{SubjectName} Exam Reminder",
                        Content = "This is a reminder that your {SubjectName} exam is scheduled for {ExamDate}. Please make sure you are prepared."
                    },
                    new NotificationTemplateDto
                    {
                        Name = "Parent Meeting",
                        Type = "Class",
                        TargetRole = "Student",
                        Priority = "Normal",
                        Title = "Parent-Teacher Meeting - {ClassName}",
                        Content = "A parent-teacher meeting for {ClassName} is scheduled for {MeetingDate}. Please inform your parents."
                    },
                    new NotificationTemplateDto
                    {
                        Name = "System Maintenance",
                        Type = "System",
                        TargetRole = "All",
                        Priority = "Normal",
                        Title = "Scheduled System Maintenance",
                        Content = "The school management system will be under maintenance on {MaintenanceDate} from {StartTime} to {EndTime}."
                    }
                };

                return Ok(ApiResponseDto<List<NotificationTemplateDto>>.SuccessResult(templates));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notification templates");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }
    }

    // DTOs for Notification Management
    public class CreateNotificationDto
    {
        public string Title { get; set; }
        public string Content { get; set; }
        public string Type { get; set; } // General, Class, Personal, System
        public string? TargetRole { get; set; } // Admin, Teacher, Student, All
        public int? ClassId { get; set; }
        public int? UserId { get; set; }
        public string? Priority { get; set; } = "Normal"; // Low, Normal, High, Urgent
        public DateTime? ExpiryDate { get; set; }
    }

    public class UpdateNotificationDto
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? Priority { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool? IsActive { get; set; }
    }

    public class NotificationResponseDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Type { get; set; }
        public string? TargetRole { get; set; }
        public int? ClassId { get; set; }
        public string? ClassName { get; set; }
        public int? UserId { get; set; }
        public string? UserName { get; set; }
        public string Priority { get; set; }
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsActive { get; set; }
    }

    public class NotificationDetailDto : NotificationResponseDto
    {
        public int EstimatedRecipients { get; set; }
    }

    public class NotificationTemplateDto
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string TargetRole { get; set; }
        public string Priority { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
    }

    public class PaginatedResponseDto<T>
    {
        public List<T> Data { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }
}