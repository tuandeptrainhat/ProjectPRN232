using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManage_API.DTOs;
using StudentManage_API.Models;
using System.Security.Claims;

namespace StudentManage_API.Controllers.Student
{
    [ApiController]
    [Route("api/student/[controller]")]
    [Authorize(Roles = "Student")]
    public class StudentScheduleController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<StudentScheduleController> _logger;

        public StudentScheduleController(StudentManagementDbContext context, ILogger<StudentScheduleController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get student's weekly schedule
        /// </summary>
        [HttpGet("weekly")]
        public async Task<IActionResult> GetWeeklySchedule(
            [FromQuery] string? schoolYear = null,
            [FromQuery] int? semester = null)
        {
            try
            {
                var studentId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Get student's class
                var studentClass = await _context.StudentClasses
                    .Include(sc => sc.Class)
                    .Where(sc => sc.StudentId == studentId && sc.IsActive == true)
                    .FirstOrDefaultAsync();

                if (studentClass == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Student is not enrolled in any class"));
                }

                // Apply school year filter
                if (!string.IsNullOrEmpty(schoolYear) && studentClass.Class.SchoolYear != schoolYear)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Student is not enrolled in the specified school year"));
                }

                var query = _context.Schedules
                    .Include(s => s.Class)
                    .Include(s => s.Subject)
                    .Include(s => s.Teacher)
                    .Where(s => s.ClassId == studentClass.ClassId && s.IsActive == true);

                // Apply semester filter
                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var schedules = await query
                    .OrderBy(s => s.DayOfWeek)
                    .ThenBy(s => s.StartTime)
                    .Select(s => new StudentScheduleDto
                    {
                        Id = s.Id,
                        SubjectId = s.SubjectId,
                        SubjectName = s.Subject.SubjectName,
                        SubjectCode = s.Subject.SubjectCode,
                        TeacherId = s.TeacherId,
                        TeacherName = s.Teacher.FullName,
                        DayOfWeek = s.DayOfWeek,
                        DayName = GetDayName(s.DayOfWeek),
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        Room = s.Room,
                        SchoolYear = s.SchoolYear,
                        Semester = s.Semester,
                        Duration = CalculateDuration(s.StartTime, s.EndTime),
                        IsActive = s.IsActive ?? false
                    })
                    .ToListAsync();

                // Group by day of week
                var weeklySchedule = new StudentWeeklyScheduleDto
                {
                    StudentId = studentId,
                    ClassId = studentClass.ClassId,
                    ClassName = studentClass.Class.ClassName,
                    Grade = studentClass.Class.Grade,
                    SchoolYear = studentClass.Class.SchoolYear,
                    Semester = semester,
                    TotalClasses = schedules.Count,
                    TotalHours = schedules.Sum(s => s.Duration),

                    ScheduleByDay = schedules
                        .GroupBy(s => s.DayOfWeek)
                        .ToDictionary(
                            g => g.Key,
                            g => new StudentDayScheduleDto
                            {
                                DayOfWeek = g.Key,
                                DayName = GetDayName(g.Key),
                                Classes = g.OrderBy(s => s.StartTime).ToList(),
                                TotalClasses = g.Count(),
                                TotalHours = g.Sum(s => s.Duration),
                                FirstClass = g.Min(s => s.StartTime),
                                LastClass = g.Max(s => s.EndTime)
                            }
                        ),

                    // Weekly summary
                    WeeklySummary = new
                    {
                        UniqueSubjects = schedules.Select(s => s.SubjectId).Distinct().Count(),
                        UniqueTeachers = schedules.Select(s => s.TeacherId).Distinct().Count(),
                        UniqueRooms = schedules.Where(s => !string.IsNullOrEmpty(s.Room))
                                               .Select(s => s.Room).Distinct().Count(),
                        BusiestDay = schedules.GroupBy(s => s.DayOfWeek)
                                            .OrderByDescending(g => g.Count())
                                            .Select(g => GetDayName(g.Key))
                                            .FirstOrDefault(),
                        EarliestClass = schedules.Min(s => s.StartTime),
                        LatestClass = schedules.Max(s => s.EndTime),
                        AverageClassesPerDay = Math.Round((double)schedules.Count / 5, 1) // Assuming 5 school days
                    }
                };

                return Ok(ApiResponseDto<StudentWeeklyScheduleDto>.SuccessResult(weeklySchedule));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting student weekly schedule");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get student's schedule for a specific day
        /// </summary>
        [HttpGet("daily")]
        public async Task<IActionResult> GetDailySchedule(
            [FromQuery] int? dayOfWeek = null,
            [FromQuery] string? schoolYear = null,
            [FromQuery] int? semester = null)
        {
            try
            {
                var studentId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var targetDay = dayOfWeek ?? (int)DateTime.Today.DayOfWeek;

                // Convert Sunday (0) to 7 to match database format
                if (targetDay == 0) targetDay = 7;

                // Get student's class
                var studentClass = await _context.StudentClasses
                    .Include(sc => sc.Class)
                    .Where(sc => sc.StudentId == studentId && sc.IsActive == true)
                    .FirstOrDefaultAsync();

                if (studentClass == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Student is not enrolled in any class"));
                }

                var query = _context.Schedules
                    .Include(s => s.Subject)
                    .Include(s => s.Teacher)
                    .Where(s => s.ClassId == studentClass.ClassId &&
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
                    .Select(s => new StudentScheduleDetailDto
                    {
                        Id = s.Id,
                        SubjectId = s.SubjectId,
                        SubjectName = s.Subject.SubjectName,
                        SubjectCode = s.Subject.SubjectCode,
                        Credits = s.Subject.Credits ?? 1,
                        TeacherId = s.TeacherId,
                        TeacherName = s.Teacher.FullName,
                        TeacherEmail = s.Teacher.Email,
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        Room = s.Room,
                        Duration = CalculateDuration(s.StartTime, s.EndTime),

                        // Get recent performance data
                        RecentAverageScore = _context.Scores
                            .Where(sc => sc.StudentId == studentId &&
                                        sc.SubjectId == s.SubjectId &&
                                        sc.CreatedDate >= DateTime.Today.AddDays(-30))
                            .Average(sc => (double?)sc.ScoreValue) ?? 0,

                        RecentAttendanceRate = _context.Attendances
                            .Where(a => a.StudentId == studentId &&
                                       a.SubjectId == s.SubjectId &&
                                       a.AttendanceDate >= DateOnly.FromDateTime(DateTime.Today.AddDays(-30)))
                            .GroupBy(a => 1)
                            .Select(g => g.Count(a => a.Status == "Present") * 100.0 / g.Count())
                            .FirstOrDefault(),

                        // Upcoming assignments/exams (if any)
                        UpcomingExams = _context.Scores
                            .Where(sc => sc.StudentId == studentId &&
                                        sc.SubjectId == s.SubjectId &&
                                        sc.ExamDate.HasValue &&
                                        sc.ExamDate >= DateOnly.FromDateTime(DateTime.Today))
                            .Take(3)
                            .Select(sc => new UpcomingExamDto
                            {
                                ScoreType = sc.ScoreType,
                                ExamDate = sc.ExamDate,
                                Note = sc.Note
                            })
                            .ToList()
                    })
                    .ToListAsync();

                var dailySchedule = new StudentDailyScheduleDto
                {
                    Date = DateTime.Today,
                    DayOfWeek = targetDay,
                    DayName = GetDayName(targetDay),
                    StudentId = studentId,
                    ClassId = studentClass.ClassId,
                    ClassName = studentClass.Class.ClassName,
                    TotalClasses = schedules.Count,
                    TotalHours = schedules.Sum(s => s.Duration),
                    Classes = schedules,

                    DaySummary = new
                    {
                        FirstClass = schedules.FirstOrDefault()?.StartTime,
                        LastClass = schedules.LastOrDefault()?.EndTime,
                        LongestBreak = CalculateLongestBreak(schedules),
                        NumberOfRooms = schedules.Where(s => !string.IsNullOrEmpty(s.Room))
                                                .Select(s => s.Room).Distinct().Count(),
                        NumberOfTeachers = schedules.Select(s => s.TeacherId).Distinct().Count(),
                        TotalCredits = schedules.Sum(s => s.Credits)
                    }
                };

                return Ok(ApiResponseDto<StudentDailyScheduleDto>.SuccessResult(dailySchedule));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting student daily schedule");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get current/next class for student
        /// </summary>
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrentClass()
        {
            try
            {
                var studentId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var now = DateTime.Now;
                var currentDay = (int)now.DayOfWeek;
                if (currentDay == 0) currentDay = 7; // Convert Sunday to 7

                var currentTime = TimeOnly.FromDateTime(now);

                // Get student's class
                var studentClass = await _context.StudentClasses
                    .Where(sc => sc.StudentId == studentId && sc.IsActive == true)
                    .FirstOrDefaultAsync();

                if (studentClass == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Student is not enrolled in any class"));
                }

                // Find current class
                var currentClassSchedule = await _context.Schedules
                    .Include(s => s.Subject)
                    .Include(s => s.Teacher)
                    .Where(s => s.ClassId == studentClass.ClassId &&
                               s.DayOfWeek == currentDay &&
                               s.StartTime <= currentTime &&
                               s.EndTime > currentTime &&
                               s.IsActive == true)
                    .Select(s => new StudentCurrentClassDto
                    {
                        Id = s.Id,
                        SubjectName = s.Subject.SubjectName,
                        SubjectCode = s.Subject.SubjectCode,
                        TeacherName = s.Teacher.FullName,
                        Room = s.Room,
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        Status = "Current",
                        TimeRemaining = CalculateTimeRemaining(s.EndTime, currentTime),
                        ClassName = studentClass.Class.ClassName
                    })
                    .FirstOrDefaultAsync();

                // If no current class, find next class
                if (currentClassSchedule == null)
                {
                    var nextClassSchedule = await _context.Schedules
                        .Include(s => s.Subject)
                        .Include(s => s.Teacher)
                        .Where(s => s.ClassId == studentClass.ClassId &&
                                   s.DayOfWeek == currentDay &&
                                   s.StartTime > currentTime &&
                                   s.IsActive == true)
                        .OrderBy(s => s.StartTime)
                        .Select(s => new StudentCurrentClassDto
                        {
                            Id = s.Id,
                            SubjectName = s.Subject.SubjectName,
                            SubjectCode = s.Subject.SubjectCode,
                            TeacherName = s.Teacher.FullName,
                            Room = s.Room,
                            StartTime = s.StartTime,
                            EndTime = s.EndTime,
                            Status = "Next",
                            TimeUntilStart = CalculateTimeUntilStart(s.StartTime, currentTime),
                            ClassName = studentClass.Class.ClassName
                        })
                        .FirstOrDefaultAsync();

                    if (nextClassSchedule != null)
                    {
                        return Ok(ApiResponseDto<StudentCurrentClassDto>.SuccessResult(nextClassSchedule));
                    }
                }
                else
                {
                    return Ok(ApiResponseDto<StudentCurrentClassDto>.SuccessResult(currentClassSchedule));
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
        /// Get student's exam schedule
        /// </summary>
        [HttpGet("exams")]
        public async Task<IActionResult> GetExamSchedule([FromQuery] int? semester = null, [FromQuery] int days = 30)
        {
            try
            {
                var studentId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var startDate = DateTime.Today;
                var endDate = DateTime.Today.AddDays(days);

                var query = _context.Scores
                    .Include(s => s.Subject)
                    .Include(s => s.CreatedByNavigation)
                    .Where(s => s.StudentId == studentId &&
                               s.ExamDate.HasValue &&
                               s.ExamDate >= DateOnly.FromDateTime(startDate) &&
                               s.ExamDate <= DateOnly.FromDateTime(endDate));

                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var exams = await query
                    .OrderBy(s => s.ExamDate)
                    .Select(s => new StudentExamDto
                    {
                        Id = s.Id,
                        SubjectId = s.SubjectId,
                        SubjectName = s.Subject.SubjectName,
                        SubjectCode = s.Subject.SubjectCode,
                        ScoreType = s.ScoreType,
                        ExamDate = s.ExamDate.Value,
                        MaxScore = s.MaxScore ?? 10,
                        TeacherName = s.CreatedByNavigation.FullName,
                        Note = s.Note,
                        Semester = s.Semester,
                        DaysUntilExam = (s.ExamDate.Value.ToDateTime(TimeOnly.MinValue) - DateTime.Today).Days,
                        HasTakenExam = s.ScoreValue > 0, // Assuming score is entered after exam
                        ScoreValue = s.ScoreValue > 0 ? s.ScoreValue : null
                    })
                    .ToListAsync();

                var examSchedule = new StudentExamScheduleDto
                {
                    StudentId = studentId,
                    Period = $"Next {days} days",
                    TotalExams = exams.Count,
                    CompletedExams = exams.Count(e => e.HasTakenExam),
                    UpcomingExams = exams.Count(e => !e.HasTakenExam),
                    Exams = exams,

                    ExamsBySubject = exams
                        .GroupBy(e => new { e.SubjectId, e.SubjectName })
                        .Select(g => new
                        {
                            SubjectId = g.Key.SubjectId,
                            SubjectName = g.Key.SubjectName,
                            ExamCount = g.Count(),
                            NextExam = g.Where(e => !e.HasTakenExam).OrderBy(e => e.ExamDate).FirstOrDefault()
                        })
                        .Cast<object>()
                        .ToList(),

                    ExamsByWeek = exams
                        .GroupBy(e => new {
                            Week = GetWeekOfYear(e.ExamDate.ToDateTime(TimeOnly.MinValue)),
                            Year = e.ExamDate.Year
                        })
                        .Select(g => new
                        {
                            Week = g.Key.Week,
                            Year = g.Key.Year,
                            ExamCount = g.Count(),
                            Exams = g.OrderBy(e => e.ExamDate).ToList()
                        })
                        .OrderBy(w => w.Year)
                        .ThenBy(w => w.Week)
                        .Cast<object>()
                        .ToList()
                };

                return Ok(ApiResponseDto<StudentExamScheduleDto>.SuccessResult(examSchedule));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting exam schedule");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get student's schedule summary
        /// </summary>
        [HttpGet("summary")]
        public async Task<IActionResult> GetScheduleSummary([FromQuery] string? schoolYear = null, [FromQuery] int? semester = null)
        {
            try
            {
                var studentId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Get student and class info
                var studentInfo = await _context.StudentClasses
                    .Include(sc => sc.Student)
                    .Include(sc => sc.Class)
                        .ThenInclude(c => c.Teacher)
                    .Where(sc => sc.StudentId == studentId && sc.IsActive == true)
                    .FirstOrDefaultAsync();

                if (studentInfo == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Student is not enrolled in any class"));
                }

                var query = _context.Schedules
                    .Include(s => s.Subject)
                    .Include(s => s.Teacher)
                    .Where(s => s.ClassId == studentInfo.ClassId && s.IsActive == true);

                if (!string.IsNullOrEmpty(schoolYear))
                {
                    query = query.Where(s => s.SchoolYear == schoolYear);
                }

                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var schedules = await query.ToListAsync();

                var summary = new StudentScheduleSummaryDto
                {
                    StudentId = studentId,
                    StudentName = studentInfo.Student.FullName,
                    StudentCode = studentInfo.Student.Username,
                    ClassId = studentInfo.ClassId,
                    ClassName = studentInfo.Class.ClassName,
                    Grade = studentInfo.Class.Grade,
                    SchoolYear = studentInfo.Class.SchoolYear,
                    HomeroomTeacher = studentInfo.Class.Teacher?.FullName,
                    Semester = semester,

                    // Basic statistics
                    TotalClassesPerWeek = schedules.Count,
                    TotalHoursPerWeek = schedules.Sum(s => CalculateDuration(s.StartTime, s.EndTime)),
                    UniqueSubjects = schedules.Select(s => s.SubjectId).Distinct().Count(),
                    UniqueTeachers = schedules.Select(s => s.TeacherId).Distinct().Count(),

                    // Time distribution
                    EarliestClass = schedules.Any() ? schedules.Min(s => s.StartTime) : TimeOnly.MinValue,
                    LatestClass = schedules.Any() ? schedules.Max(s => s.EndTime) : TimeOnly.MinValue,

                    // Subject breakdown
                    SubjectBreakdown = schedules
                        .GroupBy(s => new { s.SubjectId, s.Subject.SubjectName, s.Subject.SubjectCode })
                        .Select(g => new SubjectScheduleSummaryDto
                        {
                            SubjectId = g.Key.SubjectId,
                            SubjectName = g.Key.SubjectName,
                            SubjectCode = g.Key.SubjectCode,
                            WeeklyHours = g.Sum(s => CalculateDuration(s.StartTime, s.EndTime)),
                            ClassesPerWeek = g.Count(),
                            TeacherName = g.First().Teacher.FullName,
                            Schedule = g.Select(s => new
                            {
                                Day = GetDayName(s.DayOfWeek),
                                StartTime = s.StartTime,
                                EndTime = s.EndTime,
                                Room = s.Room
                            }).Cast<object>().ToList()
                        })
                        .OrderByDescending(s => s.WeeklyHours)
                        .ToList(),

                    // Day breakdown
                    DayBreakdown = schedules
                        .GroupBy(s => s.DayOfWeek)
                        .Select(g => new DayScheduleSummaryDto
                        {
                            DayOfWeek = g.Key,
                            DayName = GetDayName(g.Key),
                            ClassCount = g.Count(),
                            TotalHours = g.Sum(s => CalculateDuration(s.StartTime, s.EndTime)),
                            FirstClass = g.Min(s => s.StartTime),
                            LastClass = g.Max(s => s.EndTime),
                            Subjects = g.Select(s => s.Subject.SubjectName).ToList()
                        })
                        .OrderBy(d => d.DayOfWeek)
                        .ToList()
                };

                return Ok(ApiResponseDto<StudentScheduleSummaryDto>.SuccessResult(summary));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting schedule summary");
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

        private static double CalculateLongestBreak(List<StudentScheduleDetailDto> schedules)
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

        private static int GetWeekOfYear(DateTime date)
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var calendar = culture.Calendar;
            return calendar.GetWeekOfYear(date, culture.DateTimeFormat.CalendarWeekRule, culture.DateTimeFormat.FirstDayOfWeek);
        }
    }

    // DTOs for Student Schedule Management
    public class StudentScheduleDto
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int TeacherId { get; set; }
        public string TeacherName { get; set; }
        public int DayOfWeek { get; set; }
        public string DayName { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string? Room { get; set; }
        public string SchoolYear { get; set; }
        public int? Semester { get; set; }
        public double Duration { get; set; }
        public bool IsActive { get; set; }
    }

    public class StudentScheduleDetailDto : StudentScheduleDto
    {
        public int Credits { get; set; }
        public string TeacherEmail { get; set; }
        public double RecentAverageScore { get; set; }
        public double RecentAttendanceRate { get; set; }
        public List<UpcomingExamDto> UpcomingExams { get; set; } = new();
    }

    public class UpcomingExamDto
    {
        public string ScoreType { get; set; }
        public DateOnly? ExamDate { get; set; }
        public string? Note { get; set; }
    }

    public class StudentWeeklyScheduleDto
    {
        public int StudentId { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public string SchoolYear { get; set; }
        public int? Semester { get; set; }
        public int TotalClasses { get; set; }
        public double TotalHours { get; set; }
        public Dictionary<int, StudentDayScheduleDto> ScheduleByDay { get; set; } = new();
        public object WeeklySummary { get; set; } = new();
    }

    public class StudentDayScheduleDto
    {
        public int DayOfWeek { get; set; }
        public string DayName { get; set; }
        public List<StudentScheduleDto> Classes { get; set; } = new();
        public int TotalClasses { get; set; }
        public double TotalHours { get; set; }
        public TimeOnly FirstClass { get; set; }
        public TimeOnly LastClass { get; set; }
    }

    public class StudentDailyScheduleDto
    {
        public DateTime Date { get; set; }
        public int DayOfWeek { get; set; }
        public string DayName { get; set; }
        public int StudentId { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int TotalClasses { get; set; }
        public double TotalHours { get; set; }
        public List<StudentScheduleDetailDto> Classes { get; set; } = new();
        public object DaySummary { get; set; } = new();
    }

    public class StudentCurrentClassDto
    {
        public int Id { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public string TeacherName { get; set; }
        public string? Room { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string Status { get; set; } // Current, Next
        public double? TimeRemaining { get; set; } // Minutes remaining for current class
        public double? TimeUntilStart { get; set; } // Minutes until next class starts
        public string ClassName { get; set; }
    }

    public class StudentExamDto
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public string ScoreType { get; set; }
        public DateOnly ExamDate { get; set; }
        public decimal MaxScore { get; set; }
        public string TeacherName { get; set; }
        public string? Note { get; set; }
        public int? Semester { get; set; }
        public int DaysUntilExam { get; set; }
        public bool HasTakenExam { get; set; }
        public decimal? ScoreValue { get; set; }
    }

    public class StudentExamScheduleDto
    {
        public int StudentId { get; set; }
        public string Period { get; set; }
        public int TotalExams { get; set; }
        public int CompletedExams { get; set; }
        public int UpcomingExams { get; set; }
        public List<StudentExamDto> Exams { get; set; } = new();
        public List<object> ExamsBySubject { get; set; } = new();
        public List<object> ExamsByWeek { get; set; } = new();
    }

    public class StudentScheduleSummaryDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public string StudentCode { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public string SchoolYear { get; set; }
        public string? HomeroomTeacher { get; set; }
        public int? Semester { get; set; }
        public int TotalClassesPerWeek { get; set; }
        public double TotalHoursPerWeek { get; set; }
        public int UniqueSubjects { get; set; }
        public int UniqueTeachers { get; set; }
        public TimeOnly EarliestClass { get; set; }
        public TimeOnly LatestClass { get; set; }
        public List<SubjectScheduleSummaryDto> SubjectBreakdown { get; set; } = new();
        public List<DayScheduleSummaryDto> DayBreakdown { get; set; } = new();
    }

    public class SubjectScheduleSummaryDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public double WeeklyHours { get; set; }
        public int ClassesPerWeek { get; set; }
        public string TeacherName { get; set; }
        public List<object> Schedule { get; set; } = new();
    }

    public class DayScheduleSummaryDto
    {
        public int DayOfWeek { get; set; }
        public string DayName { get; set; }
        public int ClassCount { get; set; }
        public double TotalHours { get; set; }
        public TimeOnly FirstClass { get; set; }
        public TimeOnly LastClass { get; set; }
        public List<string> Subjects { get; set; } = new();
    }
}