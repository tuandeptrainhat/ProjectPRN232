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
    public class AdminClassController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<AdminClassController> _logger;

        public AdminClassController(StudentManagementDbContext context, ILogger<AdminClassController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get all classes with teacher and student information
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetClasses([FromQuery] int? grade = null, [FromQuery] string? schoolYear = null)
        {
            try
            {
                var query = _context.Classes
                    .Include(c => c.Teacher)
                    .Include(c => c.StudentClasses.Where(sc => sc.IsActive == true))
                    .AsQueryable();

                if (grade.HasValue)
                {
                    query = query.Where(c => c.Grade == grade.Value);
                }

                if (!string.IsNullOrEmpty(schoolYear))
                {
                    query = query.Where(c => c.SchoolYear == schoolYear);
                }

                var classes = await query
                    .Where(c => c.IsActive == true)
                    .Select(c => new ClassResponseDto
                    {
                        Id = c.Id,
                        ClassName = c.ClassName,
                        Grade = c.Grade,
                        TeacherId = c.TeacherId,
                        TeacherName = c.Teacher != null ? c.Teacher.FullName : null,
                        MaxStudents = c.MaxStudents ?? 40,
                        CurrentStudents = c.StudentClasses.Count(sc => sc.IsActive == true),
                        SchoolYear = c.SchoolYear,
                        IsActive = c.IsActive ?? false,
                        CreatedDate = c.CreatedDate ?? DateTime.UtcNow
                    })
                    .OrderBy(c => c.Grade)
                    .ThenBy(c => c.ClassName)
                    .ToListAsync();

                return Ok(ApiResponseDto<List<ClassResponseDto>>.SuccessResult(classes));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting classes");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get class by ID with detailed information
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetClass(int id)
        {
            try
            {
                var classEntity = await _context.Classes
                    .Include(c => c.Teacher)
                    .Include(c => c.StudentClasses.Where(sc => sc.IsActive == true))
                        .ThenInclude(sc => sc.Student)
                    .Include(c => c.ClassSubjects)
                        .ThenInclude(cs => cs.Subject)
                    .FirstOrDefaultAsync(c => c.Id == id && c.IsActive == true);

                if (classEntity == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Class not found"));
                }

                var response = new ClassDetailResponseDto
                {
                    Id = classEntity.Id,
                    ClassName = classEntity.ClassName,
                    Grade = classEntity.Grade,
                    TeacherId = classEntity.TeacherId,
                    TeacherName = classEntity.Teacher?.FullName,
                    MaxStudents = classEntity.MaxStudents ?? 40,
                    CurrentStudents = classEntity.StudentClasses.Count,
                    SchoolYear = classEntity.SchoolYear,
                    IsActive = classEntity.IsActive ?? false,
                    CreatedDate = classEntity.CreatedDate ?? DateTime.UtcNow,
                    Students = classEntity.StudentClasses.Select(sc => new StudentInClassDto
                    {
                        StudentId = sc.StudentId,
                        StudentName = sc.Student.FullName,
                        StudentEmail = sc.Student.Email,
                        EnrollDate = sc.EnrollDate ?? DateTime.UtcNow
                    }).ToList(),
                    Subjects = classEntity.ClassSubjects.Select(cs => new SubjectInClassDto
                    {
                        SubjectId = cs.SubjectId,
                        SubjectName = cs.Subject.SubjectName,
                        SubjectCode = cs.Subject.SubjectCode,
                        TeacherId = cs.TeacherId
                    }).ToList()
                };

                return Ok(ApiResponseDto<ClassDetailResponseDto>.SuccessResult(response));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting class {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Create new class
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateClass([FromBody] CreateClassDto dto)
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

                // Check if class name exists in the same school year
                if (await _context.Classes.AnyAsync(c => c.ClassName == dto.ClassName && c.SchoolYear == dto.SchoolYear && c.IsActive == true))
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Class name already exists in this school year"));
                }

                // Verify teacher exists and is a teacher
                if (dto.TeacherId.HasValue)
                {
                    var teacher = await _context.Users.FindAsync(dto.TeacherId.Value);
                    if (teacher == null || teacher.Role != "Teacher" || teacher.IsActive != true)
                    {
                        return BadRequest(ApiResponseDto<object>.ErrorResult("Invalid teacher selected"));
                    }
                }

                var classEntity = new Class
                {
                    ClassName = dto.ClassName,
                    Grade = dto.Grade,
                    TeacherId = dto.TeacherId,
                    MaxStudents = dto.MaxStudents,
                    SchoolYear = dto.SchoolYear,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };

                _context.Classes.Add(classEntity);
                await _context.SaveChangesAsync();

                var response = new ClassResponseDto
                {
                    Id = classEntity.Id,
                    ClassName = classEntity.ClassName,
                    Grade = classEntity.Grade,
                    TeacherId = classEntity.TeacherId,
                    TeacherName = dto.TeacherId.HasValue ?
                        (await _context.Users.FindAsync(dto.TeacherId.Value))?.FullName : null,
                    MaxStudents = classEntity.MaxStudents ?? 40,
                    CurrentStudents = 0,
                    SchoolYear = classEntity.SchoolYear,
                    IsActive = classEntity.IsActive ?? false,
                    CreatedDate = classEntity.CreatedDate ?? DateTime.UtcNow
                };

                return CreatedAtAction(nameof(GetClass), new { id = classEntity.Id },
                    ApiResponseDto<ClassResponseDto>.SuccessResult(response, "Class created successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating class");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Update class information
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateClass(int id, [FromBody] UpdateClassDto dto)
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

                var classEntity = await _context.Classes.FindAsync(id);
                if (classEntity == null || classEntity.IsActive != true)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Class not found"));
                }

                // Check class name uniqueness if changed
                if (!string.IsNullOrEmpty(dto.ClassName) && dto.ClassName != classEntity.ClassName)
                {
                    var schoolYear = dto.SchoolYear ?? classEntity.SchoolYear;
                    if (await _context.Classes.AnyAsync(c => c.ClassName == dto.ClassName &&
                        c.SchoolYear == schoolYear && c.Id != id && c.IsActive == true))
                    {
                        return BadRequest(ApiResponseDto<object>.ErrorResult("Class name already exists in this school year"));
                    }
                    classEntity.ClassName = dto.ClassName;
                }

                // Verify teacher if changed
                if (dto.TeacherId.HasValue && dto.TeacherId != classEntity.TeacherId)
                {
                    var teacher = await _context.Users.FindAsync(dto.TeacherId.Value);
                    if (teacher == null || teacher.Role != "Teacher" || teacher.IsActive != true)
                    {
                        return BadRequest(ApiResponseDto<object>.ErrorResult("Invalid teacher selected"));
                    }
                    classEntity.TeacherId = dto.TeacherId;
                }

                // Update other fields
                if (dto.Grade.HasValue) classEntity.Grade = dto.Grade.Value;
                if (dto.MaxStudents.HasValue) classEntity.MaxStudents = dto.MaxStudents.Value;
                if (!string.IsNullOrEmpty(dto.SchoolYear)) classEntity.SchoolYear = dto.SchoolYear;
                if (dto.IsActive.HasValue) classEntity.IsActive = dto.IsActive.Value;

                await _context.SaveChangesAsync();

                // Get updated class info
                var updatedClass = await _context.Classes
                    .Include(c => c.Teacher)
                    .Include(c => c.StudentClasses.Where(sc => sc.IsActive == true))
                    .FirstOrDefaultAsync(c => c.Id == id);

                var response = new ClassResponseDto
                {
                    Id = updatedClass.Id,
                    ClassName = updatedClass.ClassName,
                    Grade = updatedClass.Grade,
                    TeacherId = updatedClass.TeacherId,
                    TeacherName = updatedClass.Teacher?.FullName,
                    MaxStudents = updatedClass.MaxStudents ?? 40,
                    CurrentStudents = updatedClass.StudentClasses.Count,
                    SchoolYear = updatedClass.SchoolYear,
                    IsActive = updatedClass.IsActive ?? false,
                    CreatedDate = updatedClass.CreatedDate ?? DateTime.UtcNow
                };

                return Ok(ApiResponseDto<ClassResponseDto>.SuccessResult(response, "Class updated successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating class {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Delete class (soft delete)
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteClass(int id)
        {
            try
            {
                var classEntity = await _context.Classes.FindAsync(id);
                if (classEntity == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Class not found"));
                }

                // Check if class has active students
                var hasActiveStudents = await _context.StudentClasses
                    .AnyAsync(sc => sc.ClassId == id && sc.IsActive == true);

                if (hasActiveStudents)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Cannot delete class with active students"));
                }

                // Soft delete
                classEntity.IsActive = false;
                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Class deleted successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting class {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Add student to class
        /// </summary>
        [HttpPost("{classId}/students/{studentId}")]
        public async Task<IActionResult> AddStudentToClass(int classId, int studentId)
        {
            try
            {
                var classEntity = await _context.Classes
                    .Include(c => c.StudentClasses)
                    .FirstOrDefaultAsync(c => c.Id == classId && c.IsActive == true);

                if (classEntity == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Class not found"));
                }

                var student = await _context.Users.FindAsync(studentId);
                if (student == null || student.Role != "Student" || student.IsActive != true)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Invalid student"));
                }

                // Check if student already in class
                if (await _context.StudentClasses.AnyAsync(sc => sc.ClassId == classId && sc.StudentId == studentId && sc.IsActive == true))
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Student already in this class"));
                }

                // Check class capacity
                var currentStudents = classEntity.StudentClasses.Count(sc => sc.IsActive == true);
                if (currentStudents >= (classEntity.MaxStudents ?? 40))
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Class is at maximum capacity"));
                }

                var studentClass = new StudentClass
                {
                    StudentId = studentId,
                    ClassId = classId,
                    EnrollDate = DateTime.UtcNow,
                    IsActive = true
                };

                _context.StudentClasses.Add(studentClass);
                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Student added to class successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding student {studentId} to class {classId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Remove student from class
        /// </summary>
        [HttpDelete("{classId}/students/{studentId}")]
        public async Task<IActionResult> RemoveStudentFromClass(int classId, int studentId)
        {
            try
            {
                var studentClass = await _context.StudentClasses
                    .FirstOrDefaultAsync(sc => sc.ClassId == classId && sc.StudentId == studentId && sc.IsActive == true);

                if (studentClass == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Student not found in this class"));
                }

                studentClass.IsActive = false;
                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Student removed from class successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing student {studentId} from class {classId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get class statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<IActionResult> GetClassStatistics()
        {
            try
            {
                var totalClasses = await _context.Classes.CountAsync(c => c.IsActive == true);

                var byGrade = await _context.Classes
                    .Where(c => c.IsActive == true)
                    .GroupBy(c => c.Grade)
                    .Select(g => new { Grade = g.Key, Count = g.Count() })
                    .OrderBy(x => x.Grade)
                    .ToListAsync();

                var bySchoolYear = await _context.Classes
                    .Where(c => c.IsActive == true)
                    .GroupBy(c => c.SchoolYear)
                    .Select(g => new { SchoolYear = g.Key, Count = g.Count() })
                    .ToListAsync();

                var result = new
                {
                    TotalClasses = totalClasses,
                    ByGrade = byGrade,
                    BySchoolYear = bySchoolYear
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting class statistics");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }
    }

    // DTOs for Class Management
    public class CreateClassDto
    {
        public string ClassName { get; set; }
        public int Grade { get; set; } // Từ 1-12
        public int? TeacherId { get; set; }
        public int MaxStudents { get; set; } = 40;
        public string SchoolYear { get; set; } // VD: "2024-2025"
    }

    public class UpdateClassDto
    {
        public string? ClassName { get; set; }
        public int? Grade { get; set; }
        public int? TeacherId { get; set; }
        public int? MaxStudents { get; set; }
        public string? SchoolYear { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ClassResponseDto
    {
        public int Id { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public int? TeacherId { get; set; }
        public string? TeacherName { get; set; }
        public int MaxStudents { get; set; }
        public int CurrentStudents { get; set; }
        public string SchoolYear { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class ClassDetailResponseDto : ClassResponseDto
    {
        public List<StudentInClassDto> Students { get; set; } = new();
        public List<SubjectInClassDto> Subjects { get; set; } = new();
    }

    public class StudentInClassDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public string StudentEmail { get; set; }
        public DateTime EnrollDate { get; set; }
    }

    public class SubjectInClassDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int TeacherId { get; set; }
    }
}