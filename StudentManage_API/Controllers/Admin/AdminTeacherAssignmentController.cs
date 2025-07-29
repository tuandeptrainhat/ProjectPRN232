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
    public class AdminTeacherAssignmentController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<AdminTeacherAssignmentController> _logger;

        public AdminTeacherAssignmentController(StudentManagementDbContext context, ILogger<AdminTeacherAssignmentController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get all teacher assignments
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTeacherAssignments()
        {
            try
            {
                var assignments = await _context.ClassSubjects
                    .Include(cs => cs.Teacher)
                    .Include(cs => cs.Class)
                    .Include(cs => cs.Subject)
                    .Where(cs => cs.IsActive == true)
                    .Select(cs => new TeacherAssignmentDto
                    {
                        Id = cs.Id,
                        TeacherId = cs.TeacherId,
                        TeacherName = cs.Teacher.FullName,
                        TeacherEmail = cs.Teacher.Email,
                        ClassId = cs.ClassId,
                        ClassName = cs.Class.ClassName,
                        Grade = cs.Class.Grade,
                        SubjectId = cs.SubjectId,
                        SubjectName = cs.Subject.SubjectName,
                        SubjectCode = cs.Subject.SubjectCode,
                        SchoolYear = cs.Class.SchoolYear,
                        AssignedDate = cs.CreatedDate ?? DateTime.UtcNow
                    })
                    .OrderBy(ta => ta.TeacherName)
                    .ThenBy(ta => ta.ClassName)
                    .ToListAsync();

                return Ok(ApiResponseDto<List<TeacherAssignmentDto>>.SuccessResult(assignments));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting teacher assignments");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get assignments for a specific teacher
        /// </summary>
        [HttpGet("teacher/{teacherId}")]
        public async Task<IActionResult> GetAssignmentsByTeacher(int teacherId)
        {
            try
            {
                // Verify teacher exists
                var teacher = await _context.Users.FindAsync(teacherId);
                if (teacher == null || teacher.Role != "Teacher" || teacher.IsActive != true)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Teacher not found"));
                }

                var assignments = await _context.ClassSubjects
                    .Include(cs => cs.Class)
                    .Include(cs => cs.Subject)
                    .Where(cs => cs.TeacherId == teacherId && cs.IsActive == true)
                    .Select(cs => new TeacherClassSubjectDto
                    {
                        ClassSubjectId = cs.Id,
                        ClassId = cs.ClassId,
                        ClassName = cs.Class.ClassName,
                        Grade = cs.Class.Grade,
                        SubjectId = cs.SubjectId,
                        SubjectName = cs.Subject.SubjectName,
                        SubjectCode = cs.Subject.SubjectCode,
                        Credits = cs.Subject.Credits ?? 1,
                        SchoolYear = cs.Class.SchoolYear,
                        StudentCount = cs.Class.StudentClasses.Count(sc => sc.IsActive == true),
                        AssignedDate = cs.CreatedDate ?? DateTime.UtcNow
                    })
                    .OrderBy(tcs => tcs.ClassName)
                    .ThenBy(tcs => tcs.SubjectName)
                    .ToListAsync();

                var result = new TeacherAssignmentSummaryDto
                {
                    TeacherId = teacherId,
                    TeacherName = teacher.FullName,
                    TeacherEmail = teacher.Email,
                    TotalAssignments = assignments.Count,
                    TotalClasses = assignments.Select(a => a.ClassId).Distinct().Count(),
                    TotalSubjects = assignments.Select(a => a.SubjectId).Distinct().Count(),
                    Assignments = assignments
                };

                return Ok(ApiResponseDto<TeacherAssignmentSummaryDto>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting assignments for teacher {teacherId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get assignments for a specific class
        /// </summary>
        [HttpGet("class/{classId}")]
        public async Task<IActionResult> GetAssignmentsByClass(int classId)
        {
            try
            {
                var classEntity = await _context.Classes
                    .Include(c => c.Teacher)
                    .FirstOrDefaultAsync(c => c.Id == classId && c.IsActive == true);

                if (classEntity == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Class not found"));
                }

                var assignments = await _context.ClassSubjects
                    .Include(cs => cs.Teacher)
                    .Include(cs => cs.Subject)
                    .Where(cs => cs.ClassId == classId && cs.IsActive == true)
                    .Select(cs => new ClassSubjectAssignmentDto
                    {
                        ClassSubjectId = cs.Id,
                        SubjectId = cs.SubjectId,
                        SubjectName = cs.Subject.SubjectName,
                        SubjectCode = cs.Subject.SubjectCode,
                        Credits = cs.Subject.Credits ?? 1,
                        TeacherId = cs.TeacherId,
                        TeacherName = cs.Teacher.FullName,
                        TeacherEmail = cs.Teacher.Email,
                        AssignedDate = cs.CreatedDate ?? DateTime.UtcNow
                    })
                    .OrderBy(csa => csa.SubjectName)
                    .ToListAsync();

                var result = new ClassAssignmentSummaryDto
                {
                    ClassId = classId,
                    ClassName = classEntity.ClassName,
                    Grade = classEntity.Grade,
                    SchoolYear = classEntity.SchoolYear,
                    ClassTeacherId = classEntity.TeacherId,
                    ClassTeacherName = classEntity.Teacher?.FullName,
                    TotalSubjects = assignments.Count,
                    Assignments = assignments
                };

                return Ok(ApiResponseDto<ClassAssignmentSummaryDto>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting assignments for class {classId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Assign teacher to class as homeroom teacher
        /// </summary>
        [HttpPost("assign-homeroom-teacher")]
        public async Task<IActionResult> AssignHomeroomTeacher([FromBody] AssignHomeroomTeacherDto dto)
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

                // Verify class exists
                var classEntity = await _context.Classes.FindAsync(dto.ClassId);
                if (classEntity == null || classEntity.IsActive != true)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Class not found"));
                }

                // Verify teacher exists
                var teacher = await _context.Users.FindAsync(dto.TeacherId);
                if (teacher == null || teacher.Role != "Teacher" || teacher.IsActive != true)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Invalid teacher"));
                }

                // Update class homeroom teacher
                classEntity.TeacherId = dto.TeacherId;
                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Homeroom teacher assigned successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning homeroom teacher");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Assign multiple subjects to teacher for a class
        /// </summary>
        [HttpPost("assign-subjects")]
        public async Task<IActionResult> AssignSubjectsToTeacher([FromBody] AssignSubjectsToTeacherDto dto)
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

                // Verify teacher exists
                var teacher = await _context.Users.FindAsync(dto.TeacherId);
                if (teacher == null || teacher.Role != "Teacher" || teacher.IsActive != true)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Invalid teacher"));
                }

                // Verify class exists
                var classEntity = await _context.Classes.FindAsync(dto.ClassId);
                if (classEntity == null || classEntity.IsActive != true)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Class not found"));
                }

                // Verify all subjects exist
                var subjects = await _context.Subjects
                    .Where(s => dto.SubjectIds.Contains(s.Id) && s.IsActive == true)
                    .ToListAsync();

                if (subjects.Count != dto.SubjectIds.Count)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("One or more subjects not found"));
                }

                var successfulAssignments = new List<string>();
                var skippedAssignments = new List<string>();

                foreach (var subjectId in dto.SubjectIds)
                {
                    var subject = subjects.First(s => s.Id == subjectId);

                    // Check if assignment already exists
                    var existingAssignment = await _context.ClassSubjects
                        .FirstOrDefaultAsync(cs => cs.ClassId == dto.ClassId && cs.SubjectId == subjectId);

                    if (existingAssignment != null)
                    {
                        if (existingAssignment.IsActive == true)
                        {
                            skippedAssignments.Add($"{subject.SubjectName} (already assigned)");
                            continue;
                        }
                        else
                        {
                            // Reactivate existing assignment
                            existingAssignment.IsActive = true;
                            existingAssignment.TeacherId = dto.TeacherId;
                            existingAssignment.CreatedDate = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        // Create new assignment
                        var classSubject = new ClassSubject
                        {
                            ClassId = dto.ClassId,
                            SubjectId = subjectId,
                            TeacherId = dto.TeacherId,
                            IsActive = true,
                            CreatedDate = DateTime.UtcNow
                        };

                        _context.ClassSubjects.Add(classSubject);
                    }

                    successfulAssignments.Add(subject.SubjectName);
                }

                await _context.SaveChangesAsync();

                var message = $"Successfully assigned {successfulAssignments.Count} subjects";
                if (skippedAssignments.Any())
                {
                    message += $". Skipped {skippedAssignments.Count} subjects (already assigned)";
                }

                var result = new
                {
                    SuccessfulAssignments = successfulAssignments,
                    SkippedAssignments = skippedAssignments,
                    Message = message
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result, message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning subjects to teacher");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Remove teacher assignment from class-subject
        /// </summary>
        [HttpDelete("remove-assignment/{classSubjectId}")]
        public async Task<IActionResult> RemoveAssignment(int classSubjectId)
        {
            try
            {
                var assignment = await _context.ClassSubjects.FindAsync(classSubjectId);
                if (assignment == null || assignment.IsActive != true)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Assignment not found"));
                }

                // Check if there are scores for this assignment
                var hasScores = await _context.Scores
                    .AnyAsync(s => s.ClassId == assignment.ClassId && s.SubjectId == assignment.SubjectId);

                if (hasScores)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Cannot remove assignment with existing scores"));
                }

                assignment.IsActive = false;
                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Assignment removed successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing assignment {classSubjectId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get available teachers for assignment
        /// </summary>
        [HttpGet("available-teachers")]
        public async Task<IActionResult> GetAvailableTeachers()
        {
            try
            {
                var teachers = await _context.Users
                    .Where(u => u.Role == "Teacher" && u.IsActive == true)
                    .Select(u => new AvailableTeacherDto
                    {
                        Id = u.Id,
                        FullName = u.FullName,
                        Email = u.Email,
                        Phone = u.Phone,
                        HomeroomClasses = _context.Classes
                            .Where(c => c.TeacherId == u.Id && c.IsActive == true)
                            .Select(c => c.ClassName)
                            .ToList(),
                        SubjectAssignments = _context.ClassSubjects
                            .Include(cs => cs.Class)
                            .Include(cs => cs.Subject)
                            .Where(cs => cs.TeacherId == u.Id && cs.IsActive == true)
                            .Count()
                    })
                    .OrderBy(t => t.FullName)
                    .ToListAsync();

                return Ok(ApiResponseDto<List<AvailableTeacherDto>>.SuccessResult(teachers));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available teachers");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get teacher assignment statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<IActionResult> GetAssignmentStatistics()
        {
            try
            {
                var totalTeachers = await _context.Users.CountAsync(u => u.Role == "Teacher" && u.IsActive == true);
                var assignedTeachers = await _context.ClassSubjects
                    .Where(cs => cs.IsActive == true)
                    .Select(cs => cs.TeacherId)
                    .Distinct()
                    .CountAsync();

                var teacherWorkload = await _context.ClassSubjects
                    .Include(cs => cs.Teacher)
                    .Where(cs => cs.IsActive == true)
                    .GroupBy(cs => cs.TeacherId)
                    .Select(g => new
                    {
                        TeacherId = g.Key,
                        TeacherName = g.First().Teacher.FullName,
                        AssignmentCount = g.Count(),
                        ClassCount = g.Select(cs => cs.ClassId).Distinct().Count(),
                        SubjectCount = g.Select(cs => cs.SubjectId).Distinct().Count()
                    })
                    .OrderByDescending(tw => tw.AssignmentCount)
                    .Take(10)
                    .ToListAsync();

                var result = new
                {
                    TotalTeachers = totalTeachers,
                    AssignedTeachers = assignedTeachers,
                    UnassignedTeachers = totalTeachers - assignedTeachers,
                    TopTeachersByWorkload = teacherWorkload
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assignment statistics");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }
    }

    // DTOs for Teacher Assignment
    public class TeacherAssignmentDto
    {
        public int Id { get; set; }
        public int TeacherId { get; set; }
        public string TeacherName { get; set; }
        public string TeacherEmail { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public string SchoolYear { get; set; }
        public DateTime AssignedDate { get; set; }
    }

    public class TeacherClassSubjectDto
    {
        public int ClassSubjectId { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int Credits { get; set; }
        public string SchoolYear { get; set; }
        public int StudentCount { get; set; }
        public DateTime AssignedDate { get; set; }
    }

    public class ClassSubjectAssignmentDto
    {
        public int ClassSubjectId { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int Credits { get; set; }
        public int TeacherId { get; set; }
        public string TeacherName { get; set; }
        public string TeacherEmail { get; set; }
        public DateTime AssignedDate { get; set; }
    }

    public class TeacherAssignmentSummaryDto
    {
        public int TeacherId { get; set; }
        public string TeacherName { get; set; }
        public string TeacherEmail { get; set; }
        public int TotalAssignments { get; set; }
        public int TotalClasses { get; set; }
        public int TotalSubjects { get; set; }
        public List<TeacherClassSubjectDto> Assignments { get; set; } = new();
    }

    public class ClassAssignmentSummaryDto
    {
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public string SchoolYear { get; set; }
        public int? ClassTeacherId { get; set; }
        public string? ClassTeacherName { get; set; }
        public int TotalSubjects { get; set; }
        public List<ClassSubjectAssignmentDto> Assignments { get; set; } = new();
    }

    public class AssignHomeroomTeacherDto
    {
        public int ClassId { get; set; }
        public int TeacherId { get; set; }
    }

    public class AssignSubjectsToTeacherDto
    {
        public int TeacherId { get; set; }
        public int ClassId { get; set; }
        public List<int> SubjectIds { get; set; } = new();
    }

    public class AvailableTeacherDto
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string? Phone { get; set; }
        public List<string> HomeroomClasses { get; set; } = new();
        public int SubjectAssignments { get; set; }
    }
}