using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManage_API.DTOs;
using StudentManage_API.Models;
using System.Security.Claims;

namespace StudentManage_API.Controllers.Teacher
{
    [ApiController]
    [Route("api/teacher/[controller]")]
    [Authorize(Roles = "Teacher")]
    public class TeacherScheduleController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<TeacherScheduleController> _logger;

        public TeacherScheduleController(StudentManagementDbContext context, ILogger<TeacherScheduleController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get teacher's weekly schedule
        /// </summary>
        [HttpGet("weekly")]
        public async Task<IActionResult> GetWeeklySchedule(
            [FromQuery] string? schoolYear = null,
            [FromQuery] int? semester = null)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                var query = _context.Schedules
                    .Include(s => s.Class)
                    .Include(s => s.Subject)
                    .Include(s => s.Teacher)
                    .Where(s => s.TeacherId == teacherId && s.IsActive == true);

                // Apply filters
                if (!string.IsNullOrEmpty(schoolYear))
                {
                    query = query.Where(s => s.SchoolYear == schoolYear);
                }

                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var schedules = await query
                    .OrderBy(s => s.DayOfWeek)
                    .ThenBy(s => s.StartTime)
                    .Select(s => new TeacherScheduleDto
                    {
                        Id = s.Id,
                        ClassId = s.ClassId,
                        ClassName = s.Class.ClassName,
                        Grade = s.Class.Grade,
                        SubjectId = s.SubjectId,
                        SubjectName = s.Subject.SubjectName,
                        SubjectCode = s.Subject.SubjectCode,
                        DayOfWeek = s.DayOfWeek,
                        DayName = GetDayName(s.DayOfWeek),
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        Room = s.Room,
                        SchoolYear = s.SchoolYear,
                        Semester = s.Semester,
                        StudentCount = s.Class.StudentClasses.Count(sc => sc.IsActive == true),
                        Duration = CalculateDuration(s.StartTime, s.EndTime),
                        IsActive = s.IsActive ?? false,
                        CreatedDate = s.CreatedDate ?? DateTime.UtcNow
                    })
                    .ToListAsync();

                // Group by day of week for easier display
                var weeklySchedule = new WeeklyScheduleDto
                {
                    TeacherId = teacherId,
                    SchoolYear = schoolYear,
                    Semester = semester,
                    TotalClasses = schedules.Count,
                    TotalHours = schedules.Sum(s => s.Duration),
                    ScheduleByDay = schedules
                        .GroupBy(s => s.DayOfWeek)
                        .ToDictionary(
                            g => g.Key,
                            g => new DayScheduleDto
                            {
                                DayOfWeek = g.Key,
                                DayName = GetDayName(g.Key),
                                Classes = g.OrderBy(s => s.StartTime).ToList(),
                                TotalClasses = g.Count(),
                                TotalHours = g.Sum(s => s.Duration)
                            }
                        )
                };

                return Ok(ApiResponseDto<WeeklyScheduleDto>.SuccessResult(weeklySchedule));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting teacher weekly schedule");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get teacher's schedule for a specific day
        /// </summary>
        [HttpGet("daily")]
        public async Task<IActionResult> GetDailySchedule(
            [FromQuery] int? dayOfWeek = null,
            [FromQuery] string? schoolYear = null,
            [FromQuery] int? semester = null)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var targetDay = dayOfWeek ?? (int)DateTime.Today.DayOfWeek;

                // Convert Sunday (0) to 7 to match database format
                if (targetDay == 0) targetDay = 7;

                var query = _context.Schedules
                    .Include(s => s.Class)
                    .Include(s => s.Subject)
                    .Where(s => s.TeacherId == teacherId &&
                               s.DayOfWeek == targetDay &&
                               s.IsActive == true);

                // Apply filters
                if (!string.IsNullOrEmpty(schoolYear))
                {
                    query = query.Where(s => s.SchoolYear == schoolYear);
                }

                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var schedules = await query
                    .OrderBy(s => s.StartTime)
                    .Select(s => new TeacherScheduleDetailDto
                    {
                        Id = s.Id,
                        ClassId = s.ClassId,
                        ClassName = s.Class.ClassName,
                        Grade = s.Class.Grade,
                        SubjectId = s.SubjectId,
                        SubjectName = s.Subject.SubjectName,
                        SubjectCode = s.Subject.SubjectCode,
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        Room = s.Room,
                        SchoolYear = s.SchoolYear,
                        Semester = s.Semester,
                        StudentCount = s.Class.StudentClasses.Count(sc => sc.IsActive == true),
                        Duration = CalculateDuration(s.StartTime, s.EndTime),

                        // Additional details for daily view
                        ClassDetails = new ClassDetailsDto
                        {
                            MaxStudents = s.Class.MaxStudents ?? 40,
                            HomeroomTeacher = s.Class.Teacher != null ? s.Class.Teacher.FullName : null,
                            IsHomeroomClass = s.Class.TeacherId == teacherId
                        },

                        // Recent attendance for this class-subject
                        RecentAttendanceRate = _context.Attendances
                            .Where(a => a.ClassId == s.ClassId &&
                                       a.SubjectId == s.SubjectId &&
                                       a.AttendanceDate >= DateOnly.FromDateTime(DateTime.Today.AddDays(-7)))
                            .GroupBy(a => 1)
                            .Select(g => g.Count(a => a.Status == "Present") * 100.0 / g.Count())
                            .FirstOrDefault(),

                        // Recent average score for this class-subject
                        RecentAverageScore = _context.Scores
                            .Where(sc => sc.ClassId == s.ClassId &&
                                        sc.SubjectId == s.SubjectId &&
                                        sc.CreatedDate >= DateTime.Today.AddDays(-30))
                            .Average(sc => (double?)sc.ScoreValue) ?? 0
                    })
                    .ToListAsync();

                var dailySchedule = new DailyScheduleDto
                {
                    Date = DateTime.Today,
                    DayOfWeek = targetDay,
                    DayName = GetDayName(targetDay),
                    TotalClasses = schedules.Count,
                    TotalHours = schedules.Sum(s => s.Duration),
                    Classes = schedules,
                    Summary = new
                    {
                        FirstClass = schedules.FirstOrDefault()?.StartTime,
                        LastClass = schedules.LastOrDefault()?.EndTime,
                        LongestBreak = CalculateLongestBreak(schedules),
                        UniqueClassrooms = schedules.Select(s => s.Room).Distinct().Count(),
                        UniqueSubjects = schedules.Select(s => s.SubjectId).Distinct().Count()
                    }
                };

                return Ok(ApiResponseDto<DailyScheduleDto>.SuccessResult(dailySchedule));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting teacher daily schedule");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get current/next class information
        /// </summary>
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrentClass()
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var now = DateTime.Now;
                var currentDay = (int)now.DayOfWeek;
                if (currentDay == 0) currentDay = 7; // Convert Sunday to 7

                var currentTime = TimeOnly.FromDateTime(now);

                // Find current class
                var currentClass = await _context.Schedules
                    .Include(s => s.Class)
                    .Include(s => s.Subject)
                    .Where(s => s.TeacherId == teacherId &&
                               s.DayOfWeek == currentDay &&
                               s.StartTime <= currentTime &&
                               s.EndTime > currentTime &&
                               s.IsActive == true)
                    .Select(s => new CurrentClassDto
                    {
                        Id = s.Id,
                        ClassName = s.Class.ClassName,
                        SubjectName = s.Subject.SubjectName,
                        Room = s.Room,
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        StudentCount = s.Class.StudentClasses.Count(sc => sc.IsActive == true),
                        Status = "Current",
                        TimeRemaining = CalculateTimeRemaining(s.EndTime, currentTime)
                    })
                    .FirstOrDefaultAsync();

                // If no current class, find next class
                if (currentClass == null)
                {
                    var nextClass = await _context.Schedules
                        .Include(s => s.Class)
                        .Include(s => s.Subject)
                        .Where(s => s.TeacherId == teacherId &&
                                   s.DayOfWeek == currentDay &&
                                   s.StartTime > currentTime &&
                                   s.IsActive == true)
                        .OrderBy(s => s.StartTime)
                        .Select(s => new CurrentClassDto
                        {
                            Id = s.Id,
                            ClassName = s.Class.ClassName,
                            SubjectName = s.Subject.SubjectName,
                            Room = s.Room,
                            StartTime = s.StartTime,
                            EndTime = s.EndTime,
                            StudentCount = s.Class.StudentClasses.Count(sc => sc.IsActive == true),
                            Status = "Next",
                            TimeUntilStart = CalculateTimeUntilStart(s.StartTime, currentTime)
                        })
                        .FirstOrDefaultAsync();

                    if (nextClass != null)
                    {
                        return Ok(ApiResponseDto<CurrentClassDto>.SuccessResult(nextClass));
                    }
                }
                else
                {
                    return Ok(ApiResponseDto<CurrentClassDto>.SuccessResult(currentClass));
                }

                // No classes today
                return Ok(ApiResponseDto<object>.SuccessResult(new { Message = "No more classes today" }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current class");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get teacher's workload summary
        /// </summary>
        [HttpGet("workload")]
        public async Task<IActionResult> GetWorkloadSummary(
            [FromQuery] string? schoolYear = null,
            [FromQuery] int? semester = null)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                var query = _context.Schedules
                    .Include(s => s.Class)
                    .Include(s => s.Subject)
                    .Where(s => s.TeacherId == teacherId && s.IsActive == true);

                if (!string.IsNullOrEmpty(schoolYear))
                {
                    query = query.Where(s => s.SchoolYear == schoolYear);
                }

                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var schedules = await query.ToListAsync();

                var workload = new TeacherWorkloadDto
                {
                    TeacherId = teacherId,
                    SchoolYear = schoolYear,
                    Semester = semester,

                    // Basic statistics
                    TotalClassesPerWeek = schedules.Count,
                    TotalHoursPerWeek = schedules.Sum(s => CalculateDuration(s.StartTime, s.EndTime)),
                    UniqueClasses = schedules.Select(s => s.ClassId).Distinct().Count(),
                    UniqueSubjects = schedules.Select(s => s.SubjectId).Distinct().Count(),

                    // Workload by day
                    WorkloadByDay = schedules
                        .GroupBy(s => s.DayOfWeek)
                        .Select(g => new DayWorkloadDto
                        {
                            DayOfWeek = g.Key,
                            DayName = GetDayName(g.Key),
                            ClassCount = g.Count(),
                            TotalHours = g.Sum(s => CalculateDuration(s.StartTime, s.EndTime)),
                            FirstClass = g.Min(s => s.StartTime),
                            LastClass = g.Max(s => s.EndTime)
                        })
                        .OrderBy(d => d.DayOfWeek)
                        .ToList(),

                    // Workload by subject
                    WorkloadBySubject = schedules
                        .GroupBy(s => new { s.SubjectId, s.Subject.SubjectName })
                        .Select(g => new SubjectWorkloadDto
                        {
                            SubjectId = g.Key.SubjectId,
                            SubjectName = g.Key.SubjectName,
                            ClassCount = g.Count(),
                            TotalHours = g.Sum(s => CalculateDuration(s.StartTime, s.EndTime)),
                            DifferentClasses = g.Select(s => s.ClassId).Distinct().Count(),
                            TotalStudents = g.Sum(s => s.Class.StudentClasses.Count(sc => sc.IsActive == true))
                        })
                        .OrderByDescending(s => s.TotalHours)
                        .ToList(),

                    // Peak hours analysis
                    PeakHours = schedules
                        .GroupBy(s => s.StartTime.Hour)
                        .Select(g => new
                        {
                            Hour = g.Key,
                            ClassCount = g.Count(),
                            TimeSlot = $"{g.Key:D2}:00 - {g.Key + 1:D2}:00"
                        })
                        .OrderByDescending(h => h.ClassCount)
                        .Take(3)
                        .Cast<object>()
                        .ToList(),

                    // Room usage
                    RoomUsage = schedules
                        .Where(s => !string.IsNullOrEmpty(s.Room))
                        .GroupBy(s => s.Room)
                        .Select(g => new
                        {
                            Room = g.Key,
                            UsageCount = g.Count(),
                            TotalHours = g.Sum(s => CalculateDuration(s.StartTime, s.EndTime))
                        })
                        .OrderByDescending(r => r.UsageCount)
                        .Cast<object>()
                        .ToList()
                };

                // Calculate average class size
                workload.AverageClassSize = workload.WorkloadBySubject.Any() ?
                    Math.Round(workload.WorkloadBySubject.Average(s => (double)s.TotalStudents / s.DifferentClasses), 1) : 0;

                return Ok(ApiResponseDto<TeacherWorkloadDto>.SuccessResult(workload));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting teacher workload summary");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get available time slots for teacher
        /// </summary>
        [HttpGet("available-slots")]
        public async Task<IActionResult> GetAvailableTimeSlots(
            [FromQuery] int dayOfWeek,
            [FromQuery] string? schoolYear = null,
            [FromQuery] int? semester = null)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                var query = _context.Schedules
                    .Where(s => s.TeacherId == teacherId &&
                               s.DayOfWeek == dayOfWeek &&
                               s.IsActive == true);

                if (!string.IsNullOrEmpty(schoolYear))
                {
                    query = query.Where(s => s.SchoolYear == schoolYear);
                }

                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var existingSchedules = await query
                    .OrderBy(s => s.StartTime)
                    .Select(s => new { s.StartTime, s.EndTime })
                    .ToListAsync();

                // Generate all possible time slots (assuming 7:00 AM to 6:00 PM)
                var allSlots = new List<TimeSlotDto>();
                for (int hour = 7; hour < 18; hour++)
                {
                    allSlots.Add(new TimeSlotDto
                    {
                        StartTime = new TimeOnly(hour, 0),
                        EndTime = new TimeOnly(hour, 45),
                        Duration = 0.75
                    });

                    if (hour < 17) // Don't add break after last period
                    {
                        allSlots.Add(new TimeSlotDto
                        {
                            StartTime = new TimeOnly(hour, 50),
                            EndTime = new TimeOnly(hour + 1, 35),
                            Duration = 0.75
                        });
                    }
                }

                // Filter out occupied slots
                var availableSlots = allSlots.Where(slot =>
                    !existingSchedules.Any(existing =>
                        (slot.StartTime >= existing.StartTime && slot.StartTime < existing.EndTime) ||
                        (slot.EndTime > existing.StartTime && slot.EndTime <= existing.EndTime) ||
                        (slot.StartTime <= existing.StartTime && slot.EndTime >= existing.EndTime)
                    )).ToList();

                var result = new
                {
                    DayOfWeek = dayOfWeek,
                    DayName = GetDayName(dayOfWeek),
                    TotalSlots = allSlots.Count,
                    OccupiedSlots = allSlots.Count - availableSlots.Count,
                    AvailableSlots = availableSlots.Count,
                    AvailableTimeSlots = availableSlots
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available time slots");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        // Helper methods
        private static string GetDayName(int dayOfWeek)
        {
            return dayOfWeek switch
            {
                1 => "Monday",
                2 => "Tuesday",
                3 => "Wednesday",
                4 => "Thursday",
                5 => "Friday",
                6 => "Saturday",
                7 => "Sunday",
                _ => "Unknown"
            };
        }

        private static double CalculateDuration(TimeOnly startTime, TimeOnly endTime)
        {
            return (endTime - startTime).TotalHours;
        }

        private static double CalculateTimeRemaining(TimeOnly endTime, TimeOnly currentTime)
        {
            return Math.Max(0, (endTime - currentTime).TotalMinutes);
        }

        private static double CalculateTimeUntilStart(TimeOnly startTime, TimeOnly currentTime)
        {
            return Math.Max(0, (startTime - currentTime).TotalMinutes);
        }

        private static double CalculateLongestBreak(List<TeacherScheduleDetailDto> schedules)
        {
            if (schedules.Count < 2) return 0;

            double longestBreak = 0;
            for (int i = 0; i < schedules.Count - 1; i++)
            {
                var breakDuration = (schedules[i + 1].StartTime - schedules[i].EndTime).TotalMinutes;
                if (breakDuration > longestBreak)
                {
                    longestBreak = breakDuration;
                }
            }
            return longestBreak;
        }
    }

    // DTOs for Teacher Schedule Management
    public class TeacherScheduleDto
    {
        public int Id { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int DayOfWeek { get; set; }
        public string DayName { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string? Room { get; set; }
        public string SchoolYear { get; set; }
        public int? Semester { get; set; }
        public int StudentCount { get; set; }
        public double Duration { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class TeacherScheduleDetailDto : TeacherScheduleDto
    {
        public ClassDetailsDto ClassDetails { get; set; } = new();
        public double RecentAttendanceRate { get; set; }
        public double RecentAverageScore { get; set; }
    }

    public class ClassDetailsDto
    {
        public int MaxStudents { get; set; }
        public string? HomeroomTeacher { get; set; }
        public bool IsHomeroomClass { get; set; }
    }

    public class WeeklyScheduleDto
    {
        public int TeacherId { get; set; }
        public string? SchoolYear { get; set; }
        public int? Semester { get; set; }
        public int TotalClasses { get; set; }
        public double TotalHours { get; set; }
        public Dictionary<int, DayScheduleDto> ScheduleByDay { get; set; } = new();
    }

    public class DayScheduleDto
    {
        public int DayOfWeek { get; set; }
        public string DayName { get; set; }
        public List<TeacherScheduleDto> Classes { get; set; } = new();
        public int TotalClasses { get; set; }
        public double TotalHours { get; set; }
    }

    public class DailyScheduleDto
    {
        public DateTime Date { get; set; }
        public int DayOfWeek { get; set; }
        public string DayName { get; set; }
        public int TotalClasses { get; set; }
        public double TotalHours { get; set; }
        public List<TeacherScheduleDetailDto> Classes { get; set; } = new();
        public object Summary { get; set; } = new();
    }

    public class CurrentClassDto
    {
        public int Id { get; set; }
        public string ClassName { get; set; }
        public string SubjectName { get; set; }
        public string? Room { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public int StudentCount { get; set; }
        public string Status { get; set; } // Current, Next
        public double? TimeRemaining { get; set; } // Minutes remaining for current class
        public double? TimeUntilStart { get; set; } // Minutes until next class starts
    }

    public class TeacherWorkloadDto
    {
        public int TeacherId { get; set; }
        public string? SchoolYear { get; set; }
        public int? Semester { get; set; }
        public int TotalClassesPerWeek { get; set; }
        public double TotalHoursPerWeek { get; set; }
        public int UniqueClasses { get; set; }
        public int UniqueSubjects { get; set; }
        public double AverageClassSize { get; set; }
        public List<DayWorkloadDto> WorkloadByDay { get; set; } = new();
        public List<SubjectWorkloadDto> WorkloadBySubject { get; set; } = new();
        public List<object> PeakHours { get; set; } = new();
        public List<object> RoomUsage { get; set; } = new();
    }

    public class DayWorkloadDto
    {
        public int DayOfWeek { get; set; }
        public string DayName { get; set; }
        public int ClassCount { get; set; }
        public double TotalHours { get; set; }
        public TimeOnly FirstClass { get; set; }
        public TimeOnly LastClass { get; set; }
    }

    public class SubjectWorkloadDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public int ClassCount { get; set; }
        public double TotalHours { get; set; }
        public int DifferentClasses { get; set; }
        public int TotalStudents { get; set; }
    }

    public class TimeSlotDto
    {
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public double Duration { get; set; }
    }
}