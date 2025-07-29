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
    public class AdminDashboardController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<AdminDashboardController> _logger;

        public AdminDashboardController(StudentManagementDbContext context, ILogger<AdminDashboardController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get overall system statistics for admin dashboard
        /// </summary>
        [HttpGet("overview")]
        public async Task<IActionResult> GetDashboardOverview()
        {
            try
            {
                // User statistics
                var totalUsers = await _context.Users.CountAsync(u => u.IsActive == true);
                var usersByRole = await _context.Users
                    .Where(u => u.IsActive == true)
                    .GroupBy(u => u.Role)
                    .Select(g => new { Role = g.Key, Count = g.Count() })
                    .ToListAsync();

                // Class statistics
                var totalClasses = await _context.Classes.CountAsync(c => c.IsActive == true);
                var classesByGrade = await _context.Classes
                    .Where(c => c.IsActive == true)
                    .GroupBy(c => c.Grade)
                    .Select(g => new { Grade = g.Key, Count = g.Count() })
                    .OrderBy(x => x.Grade)
                    .ToListAsync();

                // Subject statistics
                var totalSubjects = await _context.Subjects.CountAsync(s => s.IsActive == true);

                // Student enrollment statistics
                var totalEnrollments = await _context.StudentClasses.CountAsync(sc => sc.IsActive == true);
                var averageClassSize = totalEnrollments > 0 && totalClasses > 0 ?
                    Math.Round((double)totalEnrollments / totalClasses, 2) : 0;

                // Teacher assignment statistics
                var assignedTeachers = await _context.ClassSubjects
                    .Where(cs => cs.IsActive == true)
                    .Select(cs => cs.TeacherId)
                    .Distinct()
                    .CountAsync();

                var totalTeachers = await _context.Users.CountAsync(u => u.Role == "Teacher" && u.IsActive == true);

                // Recent activity (last 7 days)
                var oneWeekAgo = DateTime.UtcNow.AddDays(-7);
                var recentUsers = await _context.Users
                    .CountAsync(u => u.CreatedDate >= oneWeekAgo && u.IsActive == true);
                var recentClasses = await _context.Classes
                    .CountAsync(c => c.CreatedDate >= oneWeekAgo && c.IsActive == true);

                var overview = new AdminDashboardOverviewDto
                {
                    TotalUsers = totalUsers,
                    TotalStudents = usersByRole.FirstOrDefault(x => x.Role == "Student")?.Count ?? 0,
                    TotalTeachers = usersByRole.FirstOrDefault(x => x.Role == "Teacher")?.Count ?? 0,
                    TotalAdmins = usersByRole.FirstOrDefault(x => x.Role == "Admin")?.Count ?? 0,
                    TotalClasses = totalClasses,
                    TotalSubjects = totalSubjects,
                    TotalEnrollments = totalEnrollments,
                    AverageClassSize = averageClassSize,
                    AssignedTeachers = assignedTeachers,
                    UnassignedTeachers = totalTeachers - assignedTeachers,
                    RecentUsers = recentUsers,
                    RecentClasses = recentClasses,
                    UsersByRole = usersByRole.Cast<object>().ToList(),
                    ClassesByGrade = classesByGrade.Cast<object>().ToList()
                };

                return Ok(ApiResponseDto<AdminDashboardOverviewDto>.SuccessResult(overview));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard overview");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get recent activities in the system
        /// </summary>
        [HttpGet("recent-activities")]
        public async Task<IActionResult> GetRecentActivities([FromQuery] int days = 7, [FromQuery] int limit = 20)
        {
            try
            {
                var dateThreshold = DateTime.UtcNow.AddDays(-days);
                var activities = new List<RecentActivityDto>();

                // Recent user registrations
                var recentUsers = await _context.Users
                    .Where(u => u.CreatedDate >= dateThreshold && u.IsActive == true)
                    .OrderByDescending(u => u.CreatedDate)
                    .Take(limit / 4)
                    .Select(u => new RecentActivityDto
                    {
                        Type = "User Registration",
                        Description = $"New {u.Role.ToLower()} registered: {u.FullName}",
                        Date = u.CreatedDate ?? DateTime.UtcNow,
                        RelatedId = u.Id,
                        RelatedType = "User"
                    })
                    .ToListAsync();

                activities.AddRange(recentUsers);

                // Recent class creations
                var recentClasses = await _context.Classes
                    .Where(c => c.CreatedDate >= dateThreshold && c.IsActive == true)
                    .OrderByDescending(c => c.CreatedDate)
                    .Take(limit / 4)
                    .Select(c => new RecentActivityDto
                    {
                        Type = "Class Creation",
                        Description = $"New class created: {c.ClassName} (Grade {c.Grade})",
                        Date = c.CreatedDate ?? DateTime.UtcNow,
                        RelatedId = c.Id,
                        RelatedType = "Class"
                    })
                    .ToListAsync();

                activities.AddRange(recentClasses);

                // Recent subject creations
                var recentSubjects = await _context.Subjects
                    .Where(s => s.CreatedDate >= dateThreshold && s.IsActive == true)
                    .OrderByDescending(s => s.CreatedDate)
                    .Take(limit / 4)
                    .Select(s => new RecentActivityDto
                    {
                        Type = "Subject Creation",
                        Description = $"New subject created: {s.SubjectName} ({s.SubjectCode})",
                        Date = s.CreatedDate ?? DateTime.UtcNow,
                        RelatedId = s.Id,
                        RelatedType = "Subject"
                    })
                    .ToListAsync();

                activities.AddRange(recentSubjects);

                // Recent teacher assignments
                var recentAssignments = await _context.ClassSubjects
                    .Include(cs => cs.Teacher)
                    .Include(cs => cs.Class)
                    .Include(cs => cs.Subject)
                    .Where(cs => cs.CreatedDate >= dateThreshold && cs.IsActive == true)
                    .OrderByDescending(cs => cs.CreatedDate)
                    .Take(limit / 4)
                    .Select(cs => new RecentActivityDto
                    {
                        Type = "Teacher Assignment",
                        Description = $"{cs.Teacher.FullName} assigned to teach {cs.Subject.SubjectName} in {cs.Class.ClassName}",
                        Date = cs.CreatedDate ?? DateTime.UtcNow,
                        RelatedId = cs.Id,
                        RelatedType = "Assignment"
                    })
                    .ToListAsync();

                activities.AddRange(recentAssignments);

                // Sort all activities by date and take the limit
                var sortedActivities = activities
                    .OrderByDescending(a => a.Date)
                    .Take(limit)
                    .ToList();

                return Ok(ApiResponseDto<List<RecentActivityDto>>.SuccessResult(sortedActivities));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent activities");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get system health metrics
        /// </summary>
        [HttpGet("system-health")]
        public async Task<IActionResult> GetSystemHealth()
        {
            try
            {
                // Database connectivity check
                var dbHealthy = true;
                var dbResponseTime = 0L;

                try
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    await _context.Users.FirstOrDefaultAsync();
                    stopwatch.Stop();
                    dbResponseTime = stopwatch.ElapsedMilliseconds;
                }
                catch
                {
                    dbHealthy = false;
                }

                // Data integrity checks
                var orphanedStudentClasses = await _context.StudentClasses
                    .CountAsync(sc => !_context.Users.Any(u => u.Id == sc.StudentId) ||
                                      !_context.Classes.Any(c => c.Id == sc.ClassId));

                var orphanedClassSubjects = await _context.ClassSubjects
                    .CountAsync(cs => !_context.Classes.Any(c => c.Id == cs.ClassId) ||
                                      !_context.Subjects.Any(s => s.Id == cs.SubjectId) ||
                                      !_context.Users.Any(u => u.Id == cs.TeacherId));

                var classesWithoutTeacher = await _context.Classes
                    .CountAsync(c => c.IsActive == true && c.TeacherId == null);

                var subjectsWithoutAssignment = await _context.Subjects
                    .CountAsync(s => s.IsActive == true &&
                                     !_context.ClassSubjects.Any(cs => cs.SubjectId == s.Id && cs.IsActive == true));

                var health = new SystemHealthDto
                {
                    OverallStatus = dbHealthy && orphanedStudentClasses == 0 && orphanedClassSubjects == 0 ? "Healthy" : "Warning",
                    DatabaseHealthy = dbHealthy,
                    DatabaseResponseTime = dbResponseTime,
                    DataIntegrityIssues = new List<string>(),
                    Recommendations = new List<string>()
                };

                // Add data integrity issues
                if (orphanedStudentClasses > 0)
                    health.DataIntegrityIssues.Add($"{orphanedStudentClasses} orphaned student class records");

                if (orphanedClassSubjects > 0)
                    health.DataIntegrityIssues.Add($"{orphanedClassSubjects} orphaned class subject assignments");

                // Add recommendations
                if (classesWithoutTeacher > 0)
                    health.Recommendations.Add($"Assign homeroom teachers to {classesWithoutTeacher} classes");

                if (subjectsWithoutAssignment > 0)
                    health.Recommendations.Add($"Assign {subjectsWithoutAssignment} subjects to classes");

                if (dbResponseTime > 1000)
                    health.Recommendations.Add("Database response time is slow, consider optimization");

                return Ok(ApiResponseDto<SystemHealthDto>.SuccessResult(health));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system health");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get enrollment trends over time
        /// </summary>
        [HttpGet("enrollment-trends")]
        public async Task<IActionResult> GetEnrollmentTrends([FromQuery] int months = 12)
        {
            try
            {
                var startDate = DateTime.UtcNow.AddMonths(-months);

                var enrollmentTrends = await _context.StudentClasses
                    .Where(sc => sc.EnrollDate >= startDate && sc.IsActive == true)
                    .GroupBy(sc => new {
                        Year = (sc.EnrollDate ?? DateTime.UtcNow).Year,
                        Month = (sc.EnrollDate ?? DateTime.UtcNow).Month
                    })
                    .Select(g => new EnrollmentTrendDto
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        MonthName = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMMM yyyy"),
                        EnrollmentCount = g.Count()
                    })
                    .OrderBy(et => et.Year)
                    .ThenBy(et => et.Month)
                    .ToListAsync();

                return Ok(ApiResponseDto<List<EnrollmentTrendDto>>.SuccessResult(enrollmentTrends));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting enrollment trends");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get top performing classes by various metrics
        /// </summary>
        [HttpGet("top-classes")]
        public async Task<IActionResult> GetTopClasses()
        {
            try
            {
                var classMetrics = await _context.Classes
                    .Include(c => c.StudentClasses)
                    .Include(c => c.ClassSubjects)
                    .Where(c => c.IsActive == true)
                    .Select(c => new ClassMetricDto
                    {
                        ClassId = c.Id,
                        ClassName = c.ClassName,
                        Grade = c.Grade,
                        SchoolYear = c.SchoolYear,
                        StudentCount = c.StudentClasses.Count(sc => sc.IsActive == true),
                        SubjectCount = c.ClassSubjects.Count(cs => cs.IsActive == true),
                        CapacityUtilization = c.MaxStudents != null && c.MaxStudents > 0 ?
                            Math.Round((double)c.StudentClasses.Count(sc => sc.IsActive == true) / c.MaxStudents.Value * 100, 2) : 0
                    })
                    .OrderByDescending(cm => cm.CapacityUtilization)
                    .Take(10)
                    .ToListAsync();

                return Ok(ApiResponseDto<List<ClassMetricDto>>.SuccessResult(classMetrics));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting top classes");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }
    }

    // DTOs for Dashboard
    public class AdminDashboardOverviewDto
    {
        public int TotalUsers { get; set; }
        public int TotalStudents { get; set; }
        public int TotalTeachers { get; set; }
        public int TotalAdmins { get; set; }
        public int TotalClasses { get; set; }
        public int TotalSubjects { get; set; }
        public int TotalEnrollments { get; set; }
        public double AverageClassSize { get; set; }
        public int AssignedTeachers { get; set; }
        public int UnassignedTeachers { get; set; }
        public int RecentUsers { get; set; }
        public int RecentClasses { get; set; }
        public List<object> UsersByRole { get; set; } = new();
        public List<object> ClassesByGrade { get; set; } = new();
    }

    public class RecentActivityDto
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public DateTime Date { get; set; }
        public int RelatedId { get; set; }
        public string RelatedType { get; set; }
    }

    public class SystemHealthDto
    {
        public string OverallStatus { get; set; }
        public bool DatabaseHealthy { get; set; }
        public long DatabaseResponseTime { get; set; }
        public List<string> DataIntegrityIssues { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    public class EnrollmentTrendDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; }
        public int EnrollmentCount { get; set; }
    }

    public class ClassMetricDto
    {
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public string SchoolYear { get; set; }
        public int StudentCount { get; set; }
        public int SubjectCount { get; set; }
        public double CapacityUtilization { get; set; }
    }
}