using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManage_API.Controllers.Admin;
using StudentManage_API.DTOs;
using StudentManage_API.Models;
using System.Security.Claims;

namespace StudentManage_API.Controllers.Teacher
{
    [ApiController]
    [Route("api/teacher/[controller]")]
    [Authorize(Roles = "Teacher")]
    public class TeacherAttendanceController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<TeacherAttendanceController> _logger;

        public TeacherAttendanceController(StudentManagementDbContext context, ILogger<TeacherAttendanceController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get students for attendance taking
        /// </summary>
        [HttpGet("class/{classId}/subject/{subjectId}/students")]
        public async Task<IActionResult> GetStudentsForAttendance(int classId, int subjectId, [FromQuery] DateOnly? date = null)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var attendanceDate = date ?? DateOnly.FromDateTime(DateTime.Today);

                // Verify teacher has permission to this class-subject
                var hasPermission = await _context.ClassSubjects
                    .AnyAsync(cs => cs.ClassId == classId && cs.SubjectId == subjectId &&
                                   cs.TeacherId == teacherId && cs.IsActive == true);

                if (!hasPermission)
                {
                    return Forbid("You don't have permission to take attendance for this class-subject combination");
                }

                var students = await _context.StudentClasses
                    .Include(sc => sc.Student)
                    .Where(sc => sc.ClassId == classId && sc.IsActive == true)
                    .Select(sc => new StudentAttendanceDto
                    {
                        StudentId = sc.StudentId,
                        StudentName = sc.Student.FullName,
                        StudentEmail = sc.Student.Email,
                        StudentCode = sc.Student.Username,
                        // Get existing attendance for the date
                        AttendanceStatus = _context.Attendances
                            .Where(a => a.StudentId == sc.StudentId &&
                                       a.ClassId == classId &&
                                       a.SubjectId == subjectId &&
                                       a.AttendanceDate == attendanceDate)
                            .Select(a => a.Status)
                            .FirstOrDefault(),
                        AttendanceNote = _context.Attendances
                            .Where(a => a.StudentId == sc.StudentId &&
                                       a.ClassId == classId &&
                                       a.SubjectId == subjectId &&
                                       a.AttendanceDate == attendanceDate)
                            .Select(a => a.Note)
                            .FirstOrDefault(),
                        AttendanceId = _context.Attendances
                            .Where(a => a.StudentId == sc.StudentId &&
                                       a.ClassId == classId &&
                                       a.SubjectId == subjectId &&
                                       a.AttendanceDate == attendanceDate)
                            .Select(a => a.Id)
                            .FirstOrDefault()
                    })
                    .OrderBy(s => s.StudentName)
                    .ToListAsync();

                var classInfo = await _context.Classes
                    .Include(c => c.Teacher)
                    .Where(c => c.Id == classId)
                    .Select(c => new
                    {
                        ClassName = c.ClassName,
                        Grade = c.Grade,
                        SchoolYear = c.SchoolYear
                    })
                    .FirstOrDefaultAsync();

                var subjectInfo = await _context.Subjects
                    .Where(s => s.Id == subjectId)
                    .Select(s => new
                    {
                        SubjectName = s.SubjectName,
                        SubjectCode = s.SubjectCode
                    })
                    .FirstOrDefaultAsync();

                var result = new AttendanceSessionDto
                {
                    ClassId = classId,
                    ClassName = classInfo?.ClassName,
                    Grade = classInfo?.Grade ?? 0,
                    SchoolYear = classInfo?.SchoolYear,
                    SubjectId = subjectId,
                    SubjectName = subjectInfo?.SubjectName,
                    SubjectCode = subjectInfo?.SubjectCode,
                    AttendanceDate = attendanceDate,
                    Students = students,
                    TotalStudents = students.Count,
                    PresentCount = students.Count(s => s.AttendanceStatus == "Present"),
                    AbsentCount = students.Count(s => s.AttendanceStatus == "Absent"),
                    LateCount = students.Count(s => s.AttendanceStatus == "Late"),
                    ExcusedCount = students.Count(s => s.AttendanceStatus == "Excused")
                };

                return Ok(ApiResponseDto<AttendanceSessionDto>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting students for attendance - Class: {classId}, Subject: {subjectId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Take attendance for a single student
        /// </summary>
        [HttpPost("take-attendance")]
        public async Task<IActionResult> TakeAttendance([FromBody] TakeAttendanceDto dto)
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

                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Verify teacher has permission
                var hasPermission = await _context.ClassSubjects
                    .AnyAsync(cs => cs.ClassId == dto.ClassId && cs.SubjectId == dto.SubjectId &&
                                   cs.TeacherId == teacherId && cs.IsActive == true);

                if (!hasPermission)
                {
                    return Forbid("You don't have permission to take attendance for this class-subject combination");
                }

                // Verify student is in the class
                var studentInClass = await _context.StudentClasses
                    .AnyAsync(sc => sc.StudentId == dto.StudentId && sc.ClassId == dto.ClassId && sc.IsActive == true);

                if (!studentInClass)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Student is not enrolled in this class"));
                }

                // Check if attendance already exists
                var existingAttendance = await _context.Attendances
                    .FirstOrDefaultAsync(a => a.StudentId == dto.StudentId &&
                                            a.ClassId == dto.ClassId &&
                                            a.SubjectId == dto.SubjectId &&
                                            a.AttendanceDate == dto.AttendanceDate);

                if (existingAttendance != null)
                {
                    // Update existing attendance
                    existingAttendance.Status = dto.Status;
                    existingAttendance.Note = dto.Note;
                    existingAttendance.CreatedBy = teacherId;
                    existingAttendance.CreatedDate = DateTime.UtcNow;
                }
                else
                {
                    // Create new attendance record
                    var attendance = new Attendance
                    {
                        StudentId = dto.StudentId,
                        ClassId = dto.ClassId,
                        SubjectId = dto.SubjectId,
                        AttendanceDate = dto.AttendanceDate,
                        Status = dto.Status,
                        Note = dto.Note,
                        CreatedBy = teacherId,
                        CreatedDate = DateTime.UtcNow
                    };

                    _context.Attendances.Add(attendance);
                }

                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Attendance recorded successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error taking attendance");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Take attendance for multiple students at once (batch)
        /// </summary>
        [HttpPost("batch-take-attendance")]
        public async Task<IActionResult> BatchTakeAttendance([FromBody] BatchTakeAttendanceDto dto)
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

                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Verify teacher has permission
                var hasPermission = await _context.ClassSubjects
                    .AnyAsync(cs => cs.ClassId == dto.ClassId && cs.SubjectId == dto.SubjectId &&
                                   cs.TeacherId == teacherId && cs.IsActive == true);

                if (!hasPermission)
                {
                    return Forbid("You don't have permission to take attendance for this class-subject combination");
                }

                var successfulAttendances = new List<string>();
                var failedAttendances = new List<string>();

                foreach (var attendanceData in dto.Attendances)
                {
                    try
                    {
                        // Verify student is in class
                        var studentInClass = await _context.StudentClasses
                            .AnyAsync(sc => sc.StudentId == attendanceData.StudentId &&
                                          sc.ClassId == dto.ClassId && sc.IsActive == true);

                        if (!studentInClass)
                        {
                            failedAttendances.Add($"Student ID {attendanceData.StudentId}: Not enrolled in this class");
                            continue;
                        }

                        // Check if attendance already exists
                        var existingAttendance = await _context.Attendances
                            .FirstOrDefaultAsync(a => a.StudentId == attendanceData.StudentId &&
                                                    a.ClassId == dto.ClassId &&
                                                    a.SubjectId == dto.SubjectId &&
                                                    a.AttendanceDate == dto.AttendanceDate);

                        if (existingAttendance != null)
                        {
                            // Update existing attendance
                            existingAttendance.Status = attendanceData.Status;
                            existingAttendance.Note = attendanceData.Note;
                            existingAttendance.CreatedBy = teacherId;
                            existingAttendance.CreatedDate = DateTime.UtcNow;
                        }
                        else
                        {
                            // Create new attendance record
                            var attendance = new Attendance
                            {
                                StudentId = attendanceData.StudentId,
                                ClassId = dto.ClassId,
                                SubjectId = dto.SubjectId,
                                AttendanceDate = dto.AttendanceDate,
                                Status = attendanceData.Status,
                                Note = attendanceData.Note,
                                CreatedBy = teacherId,
                                CreatedDate = DateTime.UtcNow
                            };

                            _context.Attendances.Add(attendance);
                        }

                        await _context.SaveChangesAsync();
                        successfulAttendances.Add($"Student ID {attendanceData.StudentId}: {attendanceData.Status}");
                    }
                    catch (Exception ex)
                    {
                        failedAttendances.Add($"Student ID {attendanceData.StudentId}: {ex.Message}");
                    }
                }

                var result = new
                {
                    SuccessfulAttendances = successfulAttendances,
                    FailedAttendances = failedAttendances,
                    SuccessCount = successfulAttendances.Count,
                    FailureCount = failedAttendances.Count
                };

                var message = $"Batch attendance completed: {successfulAttendances.Count} successful, {failedAttendances.Count} failed";
                return Ok(ApiResponseDto<object>.SuccessResult(result, message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch take attendance");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get attendance history for a class-subject
        /// </summary>
        [HttpGet("class/{classId}/subject/{subjectId}/history")]
        public async Task<IActionResult> GetAttendanceHistory(
            int classId,
            int subjectId,
            [FromQuery] DateOnly? fromDate = null,
            [FromQuery] DateOnly? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Verify teacher has permission
                var hasPermission = await _context.ClassSubjects
                    .AnyAsync(cs => cs.ClassId == classId && cs.SubjectId == subjectId &&
                                   cs.TeacherId == teacherId && cs.IsActive == true);

                if (!hasPermission)
                {
                    return Forbid("You don't have permission to view attendance for this class-subject combination");
                }

                var startDate = fromDate ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
                var endDate = toDate ?? DateOnly.FromDateTime(DateTime.Today);

                var query = _context.Attendances
                    .Include(a => a.Student)
                    .Where(a => a.ClassId == classId &&
                               a.SubjectId == subjectId &&
                               a.AttendanceDate >= startDate &&
                               a.AttendanceDate <= endDate);

                var totalCount = await query.CountAsync();

                var attendances = await query
                    .OrderByDescending(a => a.AttendanceDate)
                    .ThenBy(a => a.Student.FullName)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(a => new AttendanceHistoryDto
                    {
                        Id = a.Id,
                        StudentId = a.StudentId,
                        StudentName = a.Student.FullName,
                        AttendanceDate = a.AttendanceDate,
                        Status = a.Status,
                        Note = a.Note,
                        RecordedDate = a.CreatedDate ?? DateTime.UtcNow
                    })
                    .ToListAsync();

                var result = new PaginatedResponseDto<AttendanceHistoryDto>
                {
                    Data = attendances,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                };

                return Ok(ApiResponseDto<PaginatedResponseDto<AttendanceHistoryDto>>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting attendance history for class {classId}, subject {subjectId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get attendance statistics for a class-subject
        /// </summary>
        [HttpGet("class/{classId}/subject/{subjectId}/statistics")]
        public async Task<IActionResult> GetAttendanceStatistics(
            int classId,
            int subjectId,
            [FromQuery] DateOnly? fromDate = null,
            [FromQuery] DateOnly? toDate = null)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Verify teacher has permission
                var hasPermission = await _context.ClassSubjects
                    .AnyAsync(cs => cs.ClassId == classId && cs.SubjectId == subjectId &&
                                   cs.TeacherId == teacherId && cs.IsActive == true);

                if (!hasPermission)
                {
                    return Forbid("You don't have permission to view attendance statistics for this class-subject combination");
                }

                var startDate = fromDate ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
                var endDate = toDate ?? DateOnly.FromDateTime(DateTime.Today);

                var attendances = await _context.Attendances
                    .Include(a => a.Student)
                    .Where(a => a.ClassId == classId &&
                               a.SubjectId == subjectId &&
                               a.AttendanceDate >= startDate &&
                               a.AttendanceDate <= endDate)
                    .ToListAsync();

                if (!attendances.Any())
                {
                    return Ok(ApiResponseDto<object>.SuccessResult(new { Message = "No attendance records found" }));
                }

                var totalRecords = attendances.Count;
                var studentCount = await _context.StudentClasses.CountAsync(sc => sc.ClassId == classId && sc.IsActive == true);

                var overallStats = new
                {
                    TotalRecords = totalRecords,
                    TotalStudents = studentCount,
                    PresentCount = attendances.Count(a => a.Status == "Present"),
                    AbsentCount = attendances.Count(a => a.Status == "Absent"),
                    LateCount = attendances.Count(a => a.Status == "Late"),
                    ExcusedCount = attendances.Count(a => a.Status == "Excused"),
                    AttendanceRate = totalRecords > 0 ? Math.Round((double)attendances.Count(a => a.Status == "Present") / totalRecords * 100, 2) : 0
                };

                var byStudent = attendances
                    .GroupBy(a => new { a.StudentId, a.Student.FullName })
                    .Select(g => new
                    {
                        StudentId = g.Key.StudentId,
                        StudentName = g.Key.FullName,
                        TotalRecords = g.Count(),
                        PresentCount = g.Count(a => a.Status == "Present"),
                        AbsentCount = g.Count(a => a.Status == "Absent"),
                        LateCount = g.Count(a => a.Status == "Late"),
                        ExcusedCount = g.Count(a => a.Status == "Excused"),
                        AttendanceRate = Math.Round((double)g.Count(a => a.Status == "Present") / g.Count() * 100, 2)
                    })
                    .OrderByDescending(s => s.AttendanceRate)
                    .ToList();

                var byDate = attendances
                    .GroupBy(a => a.AttendanceDate)
                    .Select(g => new
                    {
                        Date = g.Key,
                        TotalRecords = g.Count(),
                        PresentCount = g.Count(a => a.Status == "Present"),
                        AbsentCount = g.Count(a => a.Status == "Absent"),
                        LateCount = g.Count(a => a.Status == "Late"),
                        ExcusedCount = g.Count(a => a.Status == "Excused"),
                        AttendanceRate = Math.Round((double)g.Count(a => a.Status == "Present") / g.Count() * 100, 2)
                    })
                    .OrderByDescending(d => d.Date)
                    .Take(10)
                    .ToList();

                var result = new
                {
                    Period = new { FromDate = startDate, ToDate = endDate },
                    OverallStatistics = overallStats,
                    StudentStatistics = byStudent,
                    DailyStatistics = byDate
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting attendance statistics for class {classId}, subject {subjectId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get attendance dates for a class-subject (calendar view)
        /// </summary>
        [HttpGet("class/{classId}/subject/{subjectId}/calendar")]
        public async Task<IActionResult> GetAttendanceCalendar(
            int classId,
            int subjectId,
            [FromQuery] int year = 0,
            [FromQuery] int month = 0)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Verify teacher has permission
                var hasPermission = await _context.ClassSubjects
                    .AnyAsync(cs => cs.ClassId == classId && cs.SubjectId == subjectId &&
                                   cs.TeacherId == teacherId && cs.IsActive == true);

                if (!hasPermission)
                {
                    return Forbid("You don't have permission to view attendance for this class-subject combination");
                }

                var targetYear = year == 0 ? DateTime.Today.Year : year;
                var targetMonth = month == 0 ? DateTime.Today.Month : month;

                var startDate = new DateTime(targetYear, targetMonth, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                var attendanceDates = await _context.Attendances
                    .Where(a => a.ClassId == classId &&
                               a.SubjectId == subjectId &&
                               a.AttendanceDate >= DateOnly.FromDateTime(startDate) &&
                               a.AttendanceDate <= DateOnly.FromDateTime(endDate))
                    .GroupBy(a => a.AttendanceDate)
                    .Select(g => new
                    {
                        Date = g.Key,
                        TotalRecords = g.Count(),
                        PresentCount = g.Count(a => a.Status == "Present"),
                        AbsentCount = g.Count(a => a.Status == "Absent"),
                        LateCount = g.Count(a => a.Status == "Late"),
                        ExcusedCount = g.Count(a => a.Status == "Excused"),
                        HasAttendance = true
                    })
                    .OrderBy(ad => ad.Date)
                    .ToListAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(attendanceDates));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting attendance calendar for class {classId}, subject {subjectId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }
    }

    // DTOs for Teacher Attendance Management
    public class StudentAttendanceDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public string StudentEmail { get; set; }
        public string StudentCode { get; set; }
        public string? AttendanceStatus { get; set; } // Present, Absent, Late, Excused
        public string? AttendanceNote { get; set; }
        public int? AttendanceId { get; set; }
    }

    public class AttendanceSessionDto
    {
        public int ClassId { get; set; }
        public string? ClassName { get; set; }
        public int Grade { get; set; }
        public string? SchoolYear { get; set; }
        public int SubjectId { get; set; }
        public string? SubjectName { get; set; }
        public string? SubjectCode { get; set; }
        public DateOnly AttendanceDate { get; set; }
        public List<StudentAttendanceDto> Students { get; set; } = new();
        public int TotalStudents { get; set; }
        public int PresentCount { get; set; }
        public int AbsentCount { get; set; }
        public int LateCount { get; set; }
        public int ExcusedCount { get; set; }
    }

    public class TakeAttendanceDto
    {
        public int StudentId { get; set; }
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public DateOnly AttendanceDate { get; set; }
        public string Status { get; set; } // Present, Absent, Late, Excused
        public string? Note { get; set; }
    }

    public class BatchAttendanceDataDto
    {
        public int StudentId { get; set; }
        public string Status { get; set; } // Present, Absent, Late, Excused
        public string? Note { get; set; }
    }

    public class BatchTakeAttendanceDto
    {
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public DateOnly AttendanceDate { get; set; }
        public List<BatchAttendanceDataDto> Attendances { get; set; } = new();
    }

    public class AttendanceHistoryDto
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public DateOnly AttendanceDate { get; set; }
        public string Status { get; set; }
        public string? Note { get; set; }
        public DateTime RecordedDate { get; set; }
    }
}