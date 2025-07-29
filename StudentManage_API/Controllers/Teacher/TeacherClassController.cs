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
    public class TeacherClassController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<TeacherClassController> _logger;

        public TeacherClassController(StudentManagementDbContext context, ILogger<TeacherClassController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get all classes that teacher is assigned to
        /// </summary>
        [HttpGet("my-classes")]
        public async Task<IActionResult> GetMyClasses()
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                var classes = await _context.ClassSubjects
                    .Include(cs => cs.Class)
                        .ThenInclude(c => c.StudentClasses.Where(sc => sc.IsActive == true))
                    .Include(cs => cs.Subject)
                    .Where(cs => cs.TeacherId == teacherId && cs.IsActive == true)
                    .GroupBy(cs => cs.Class)
                    .Select(g => new TeacherClassSummaryDto
                    {
                        ClassId = g.Key.Id,
                        ClassName = g.Key.ClassName,
                        Grade = g.Key.Grade,
                        SchoolYear = g.Key.SchoolYear,
                        MaxStudents = g.Key.MaxStudents ?? 40,
                        CurrentStudents = g.Key.StudentClasses.Count(sc => sc.IsActive == true),
                        IsHomeroomTeacher = g.Key.TeacherId == teacherId,
                        SubjectsTeaching = g.Select(cs => new SubjectTeachingDto
                        {
                            SubjectId = cs.SubjectId,
                            SubjectName = cs.Subject.SubjectName,
                            SubjectCode = cs.Subject.SubjectCode,
                            Credits = cs.Subject.Credits ?? 1
                        }).ToList(),
                        TotalSubjects = g.Count()
                    })
                    .OrderBy(c => c.ClassName)
                    .ToListAsync();

                return Ok(ApiResponseDto<List<TeacherClassSummaryDto>>.SuccessResult(classes));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting teacher classes");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get detailed information about a specific class
        /// </summary>
        [HttpGet("{classId}")]
        public async Task<IActionResult> GetClassDetails(int classId)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Verify teacher has access to this class
                var hasAccess = await _context.ClassSubjects
                    .AnyAsync(cs => cs.ClassId == classId && cs.TeacherId == teacherId && cs.IsActive == true) ||
                    await _context.Classes
                    .AnyAsync(c => c.Id == classId && c.TeacherId == teacherId && c.IsActive == true);

                if (!hasAccess)
                {
                    return Forbid("You don't have access to this class");
                }

                var classDetail = await _context.Classes
                    .Include(c => c.Teacher)
                    .Include(c => c.StudentClasses.Where(sc => sc.IsActive == true))
                        .ThenInclude(sc => sc.Student)
                    .Include(c => c.ClassSubjects.Where(cs => cs.IsActive == true))
                        .ThenInclude(cs => cs.Subject)
                    .Include(c => c.ClassSubjects.Where(cs => cs.IsActive == true))
                        .ThenInclude(cs => cs.Teacher)
                    .Where(c => c.Id == classId && c.IsActive == true)
                    .Select(c => new TeacherClassDetailDto
                    {
                        ClassId = c.Id,
                        ClassName = c.ClassName,
                        Grade = c.Grade,
                        SchoolYear = c.SchoolYear,
                        MaxStudents = c.MaxStudents ?? 40,
                        CurrentStudents = c.StudentClasses.Count,
                        HomeroomTeacherId = c.TeacherId,
                        HomeroomTeacherName = c.Teacher != null ? c.Teacher.FullName : null,
                        IsHomeroomTeacher = c.TeacherId == teacherId,
                        CreatedDate = c.CreatedDate ?? DateTime.UtcNow,

                        // Students in class
                        Students = c.StudentClasses.Select(sc => new StudentInClassDetailDto
                        {
                            StudentId = sc.StudentId,
                            StudentName = sc.Student.FullName,
                            StudentEmail = sc.Student.Email,
                            StudentCode = sc.Student.Username,
                            EnrollDate = sc.EnrollDate ?? DateTime.UtcNow,
                            IsActive = sc.IsActive ?? false
                        }).OrderBy(s => s.StudentName).ToList(),

                        // Subjects and teachers
                        SubjectAssignments = c.ClassSubjects.Select(cs => new ClassSubjectDetailDto
                        {
                            SubjectId = cs.SubjectId,
                            SubjectName = cs.Subject.SubjectName,
                            SubjectCode = cs.Subject.SubjectCode,
                            Credits = cs.Subject.Credits ?? 1,
                            TeacherId = cs.TeacherId,
                            TeacherName = cs.Teacher.FullName,
                            IsMySubject = cs.TeacherId == teacherId,
                            AssignedDate = cs.CreatedDate ?? DateTime.UtcNow
                        }).OrderBy(sa => sa.SubjectName).ToList()
                    })
                    .FirstOrDefaultAsync();

                if (classDetail == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Class not found"));
                }

                return Ok(ApiResponseDto<TeacherClassDetailDto>.SuccessResult(classDetail));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting class details for class {classId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get students in a class with filtering options
        /// </summary>
        [HttpGet("{classId}/students")]
        public async Task<IActionResult> GetClassStudents(
            int classId,
            [FromQuery] string? search = null,
            [FromQuery] string? sortBy = "name",
            [FromQuery] string? sortOrder = "asc",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Verify teacher has access to this class
                var hasAccess = await _context.ClassSubjects
                    .AnyAsync(cs => cs.ClassId == classId && cs.TeacherId == teacherId && cs.IsActive == true) ||
                    await _context.Classes
                    .AnyAsync(c => c.Id == classId && c.TeacherId == teacherId && c.IsActive == true);

                if (!hasAccess)
                {
                    return Forbid("You don't have access to this class");
                }

                var query = _context.StudentClasses
                    .Include(sc => sc.Student)
                    .Where(sc => sc.ClassId == classId && sc.IsActive == true);

                // Apply search filter
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(sc =>
                        sc.Student.FullName.Contains(search) ||
                        sc.Student.Email.Contains(search) ||
                        sc.Student.Username.Contains(search));
                }

                // Apply sorting
                query = sortBy?.ToLower() switch
                {
                    "email" => sortOrder == "desc" ? query.OrderByDescending(sc => sc.Student.Email) : query.OrderBy(sc => sc.Student.Email),
                    "enrolldate" => sortOrder == "desc" ? query.OrderByDescending(sc => sc.EnrollDate) : query.OrderBy(sc => sc.EnrollDate),
                    "code" => sortOrder == "desc" ? query.OrderByDescending(sc => sc.Student.Username) : query.OrderBy(sc => sc.Student.Username),
                    _ => sortOrder == "desc" ? query.OrderByDescending(sc => sc.Student.FullName) : query.OrderBy(sc => sc.Student.FullName)
                };

                var totalCount = await query.CountAsync();

                var students = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(sc => new StudentInClassDetailDto
                    {
                        StudentId = sc.StudentId,
                        StudentName = sc.Student.FullName,
                        StudentEmail = sc.Student.Email,
                        StudentCode = sc.Student.Username,
                        StudentPhone = sc.Student.Phone,
                        StudentAddress = sc.Student.Address,
                        EnrollDate = sc.EnrollDate ?? DateTime.UtcNow,
                        IsActive = sc.IsActive ?? false,

                        // Get recent scores for subjects teacher teaches
                        RecentScores = _context.Scores
                            .Include(s => s.Subject)
                            .Where(s => s.StudentId == sc.StudentId &&
                                       s.ClassId == classId &&
                                       _context.ClassSubjects.Any(cs => cs.ClassId == classId &&
                                                                        cs.SubjectId == s.SubjectId &&
                                                                        cs.TeacherId == teacherId &&
                                                                        cs.IsActive == true))
                            .OrderByDescending(s => s.CreatedDate)
                            .Take(5)
                            .Select(s => new StudentRecentScoreDto
                            {
                                SubjectName = s.Subject.SubjectName,
                                ScoreType = s.ScoreType,
                                ScoreValue = s.ScoreValue,
                                MaxScore = s.MaxScore ?? 10,
                                ExamDate = s.ExamDate,
                                CreatedDate = s.CreatedDate ?? DateTime.UtcNow
                            })
                            .ToList(),

                        // Get recent attendance for subjects teacher teaches
                        RecentAttendanceRate = _context.Attendances
                            .Where(a => a.StudentId == sc.StudentId &&
                                       a.ClassId == classId &&
                                       a.AttendanceDate >= DateOnly.FromDateTime(DateTime.Today.AddDays(-30)) &&
                                       _context.ClassSubjects.Any(cs => cs.ClassId == classId &&
                                                                        cs.SubjectId == a.SubjectId &&
                                                                        cs.TeacherId == teacherId &&
                                                                        cs.IsActive == true))
                            .GroupBy(a => a.StudentId)
                            .Select(g => Math.Round((double)g.Count(a => a.Status == "Present") / g.Count() * 100, 1))
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                var result = new PaginatedResponseDto<StudentInClassDetailDto>
                {
                    Data = students,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                };

                return Ok(ApiResponseDto<PaginatedResponseDto<StudentInClassDetailDto>>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting students for class {classId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get class performance summary
        /// </summary>
        [HttpGet("{classId}/performance")]
        public async Task<IActionResult> GetClassPerformance(int classId, [FromQuery] int? semester = null)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Verify teacher has access to this class
                var hasAccess = await _context.ClassSubjects
                    .AnyAsync(cs => cs.ClassId == classId && cs.TeacherId == teacherId && cs.IsActive == true) ||
                    await _context.Classes
                    .AnyAsync(c => c.Id == classId && c.TeacherId == teacherId && c.IsActive == true);

                if (!hasAccess)
                {
                    return Forbid("You don't have access to this class");
                }

                // Get subjects that teacher teaches in this class
                var teacherSubjects = await _context.ClassSubjects
                    .Include(cs => cs.Subject)
                    .Where(cs => cs.ClassId == classId && cs.TeacherId == teacherId && cs.IsActive == true)
                    .ToListAsync();

                var performance = new List<SubjectPerformanceDto>();

                foreach (var cs in teacherSubjects)
                {
                    var scoresQuery = _context.Scores
                        .Where(s => s.ClassId == classId && s.SubjectId == cs.SubjectId);

                    if (semester.HasValue)
                    {
                        scoresQuery = scoresQuery.Where(s => s.Semester == semester.Value);
                    }

                    var scores = await scoresQuery.ToListAsync();

                    if (scores.Any())
                    {
                        var subjectPerformance = new SubjectPerformanceDto
                        {
                            SubjectId = cs.SubjectId,
                            SubjectName = cs.Subject.SubjectName,
                            SubjectCode = cs.Subject.SubjectCode,
                            TotalScores = scores.Count,
                            AverageScore = Math.Round(scores.Average(s => (double)s.ScoreValue), 2),
                            HighestScore = scores.Max(s => s.ScoreValue),
                            LowestScore = scores.Min(s => s.ScoreValue),
                            PassingRate = Math.Round((double)scores.Count(s => s.ScoreValue >= 5) / scores.Count * 100, 2),
                            ExcellentCount = scores.Count(s => s.ScoreValue >= 8),
                            GoodCount = scores.Count(s => s.ScoreValue >= 6.5m && s.ScoreValue < 8),
                            AverageCount = scores.Count(s => s.ScoreValue >= 5 && s.ScoreValue < 6.5m),
                            BelowAverageCount = scores.Count(s => s.ScoreValue < 5)
                        };

                        performance.Add(subjectPerformance);
                    }
                }

                // Get attendance data
                var attendanceData = await _context.Attendances
                    .Where(a => a.ClassId == classId &&
                               teacherSubjects.Any(ts => ts.SubjectId == a.SubjectId) &&
                               a.AttendanceDate >= DateOnly.FromDateTime(DateTime.Today.AddDays(-30)))
                    .ToListAsync();

                var attendanceSummary = new
                {
                    TotalRecords = attendanceData.Count,
                    PresentCount = attendanceData.Count(a => a.Status == "Present"),
                    AbsentCount = attendanceData.Count(a => a.Status == "Absent"),
                    LateCount = attendanceData.Count(a => a.Status == "Late"),
                    ExcusedCount = attendanceData.Count(a => a.Status == "Excused"),
                    AttendanceRate = attendanceData.Count > 0 ?
                        Math.Round((double)attendanceData.Count(a => a.Status == "Present") / attendanceData.Count * 100, 2) : 0
                };

                var result = new
                {
                    ClassId = classId,
                    Semester = semester,
                    SubjectPerformance = performance,
                    AttendanceSummary = attendanceSummary,
                    OverallSummary = new
                    {
                        TotalSubjectsTeaching = teacherSubjects.Count,
                        OverallAverageScore = performance.Any() ? Math.Round(performance.Average(p => p.AverageScore), 2) : 0,
                        OverallPassingRate = performance.Any() ? Math.Round(performance.Average(p => p.PassingRate), 2) : 0
                    }
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting class performance for class {classId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Search students across all teacher's classes
        /// </summary>
        [HttpGet("search-students")]
        public async Task<IActionResult> SearchStudents([FromQuery] string query, [FromQuery] int limit = 10)
        {
            try
            {
                if (string.IsNullOrEmpty(query) || query.Length < 2)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Search query must be at least 2 characters"));
                }

                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Get classes that teacher has access to
                var teacherClassIds = await _context.ClassSubjects
                    .Where(cs => cs.TeacherId == teacherId && cs.IsActive == true)
                    .Select(cs => cs.ClassId)
                    .Union(
                        _context.Classes
                        .Where(c => c.TeacherId == teacherId && c.IsActive == true)
                        .Select(c => c.Id)
                    )
                    .Distinct()
                    .ToListAsync();

                var students = await _context.StudentClasses
                    .Include(sc => sc.Student)
                    .Include(sc => sc.Class)
                    .Where(sc => teacherClassIds.Contains(sc.ClassId) &&
                               sc.IsActive == true &&
                               (sc.Student.FullName.Contains(query) ||
                                sc.Student.Email.Contains(query) ||
                                sc.Student.Username.Contains(query)))
                    .Take(limit)
                    .Select(sc => new
                    {
                        StudentId = sc.StudentId,
                        StudentName = sc.Student.FullName,
                        StudentEmail = sc.Student.Email,
                        StudentCode = sc.Student.Username,
                        ClassId = sc.ClassId,
                        ClassName = sc.Class.ClassName,
                        Grade = sc.Class.Grade
                    })
                    .OrderBy(s => s.StudentName)
                    .ToListAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(students));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching students");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }
    }

    // DTOs for Teacher Class Management
    public class TeacherClassSummaryDto
    {
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public string SchoolYear { get; set; }
        public int MaxStudents { get; set; }
        public int CurrentStudents { get; set; }
        public bool IsHomeroomTeacher { get; set; }
        public List<SubjectTeachingDto> SubjectsTeaching { get; set; } = new();
        public int TotalSubjects { get; set; }
    }

    public class SubjectTeachingDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int Credits { get; set; }
    }

    public class TeacherClassDetailDto
    {
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public string SchoolYear { get; set; }
        public int MaxStudents { get; set; }
        public int CurrentStudents { get; set; }
        public int? HomeroomTeacherId { get; set; }
        public string? HomeroomTeacherName { get; set; }
        public bool IsHomeroomTeacher { get; set; }
        public DateTime CreatedDate { get; set; }
        public List<StudentInClassDetailDto> Students { get; set; } = new();
        public List<ClassSubjectDetailDto> SubjectAssignments { get; set; } = new();
    }

    public class StudentInClassDetailDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public string StudentEmail { get; set; }
        public string StudentCode { get; set; }
        public string? StudentPhone { get; set; }
        public string? StudentAddress { get; set; }
        public DateTime EnrollDate { get; set; }
        public bool IsActive { get; set; }
        public List<StudentRecentScoreDto> RecentScores { get; set; } = new();
        public double RecentAttendanceRate { get; set; }
    }

    public class StudentRecentScoreDto
    {
        public string SubjectName { get; set; }
        public string ScoreType { get; set; }
        public decimal ScoreValue { get; set; }
        public decimal MaxScore { get; set; }
        public DateOnly? ExamDate { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class ClassSubjectDetailDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int Credits { get; set; }
        public int TeacherId { get; set; }
        public string TeacherName { get; set; }
        public bool IsMySubject { get; set; }
        public DateTime AssignedDate { get; set; }
    }

    public class SubjectPerformanceDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int TotalScores { get; set; }
        public double AverageScore { get; set; }
        public decimal HighestScore { get; set; }
        public decimal LowestScore { get; set; }
        public double PassingRate { get; set; }
        public int ExcellentCount { get; set; }
        public int GoodCount { get; set; }
        public int AverageCount { get; set; }
        public int BelowAverageCount { get; set; }
    }
}