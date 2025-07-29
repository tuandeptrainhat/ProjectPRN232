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
    public class AdminSubjectController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<AdminSubjectController> _logger;

        public AdminSubjectController(StudentManagementDbContext context, ILogger<AdminSubjectController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get all subjects
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSubjects([FromQuery] bool includeInactive = false)
        {
            try
            {
                var query = _context.Subjects.AsQueryable();

                if (!includeInactive)
                {
                    query = query.Where(s => s.IsActive == true);
                }

                var subjects = await query
                    .Select(s => new SubjectResponseDto
                    {
                        Id = s.Id,
                        SubjectName = s.SubjectName,
                        SubjectCode = s.SubjectCode,
                        Credits = s.Credits ?? 1,
                        Description = s.Description,
                        IsActive = s.IsActive ?? false,
                        CreatedDate = s.CreatedDate ?? DateTime.UtcNow
                    })
                    .OrderBy(s => s.SubjectCode)
                    .ToListAsync();

                return Ok(ApiResponseDto<List<SubjectResponseDto>>.SuccessResult(subjects));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subjects");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get subject by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetSubject(int id)
        {
            try
            {
                var subject = await _context.Subjects
                    .Where(s => s.Id == id && s.IsActive == true)
                    .Select(s => new SubjectResponseDto
                    {
                        Id = s.Id,
                        SubjectName = s.SubjectName,
                        SubjectCode = s.SubjectCode,
                        Credits = s.Credits ?? 1,
                        Description = s.Description,
                        IsActive = s.IsActive ?? false,
                        CreatedDate = s.CreatedDate ?? DateTime.UtcNow
                    })
                    .FirstOrDefaultAsync();

                if (subject == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Subject not found"));
                }

                return Ok(ApiResponseDto<SubjectResponseDto>.SuccessResult(subject));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting subject {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Create new subject
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateSubject([FromBody] CreateSubjectDto dto)
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

                // Check if subject code exists
                if (await _context.Subjects.AnyAsync(s => s.SubjectCode == dto.SubjectCode && s.IsActive == true))
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Subject code already exists"));
                }

                var subject = new Subject
                {
                    SubjectName = dto.SubjectName,
                    SubjectCode = dto.SubjectCode.ToUpper(), // Standardize to uppercase
                    Credits = dto.Credits,
                    Description = dto.Description,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };

                _context.Subjects.Add(subject);
                await _context.SaveChangesAsync();

                var response = new SubjectResponseDto
                {
                    Id = subject.Id,
                    SubjectName = subject.SubjectName,
                    SubjectCode = subject.SubjectCode,
                    Credits = subject.Credits ?? 1,
                    Description = subject.Description,
                    IsActive = subject.IsActive ?? false,
                    CreatedDate = subject.CreatedDate ?? DateTime.UtcNow
                };

                return CreatedAtAction(nameof(GetSubject), new { id = subject.Id },
                    ApiResponseDto<SubjectResponseDto>.SuccessResult(response, "Subject created successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating subject");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Update subject
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateSubject(int id, [FromBody] UpdateSubjectDto dto)
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

                var subject = await _context.Subjects.FindAsync(id);
                if (subject == null || subject.IsActive != true)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Subject not found"));
                }

                // Check subject code uniqueness if changed
                if (!string.IsNullOrEmpty(dto.SubjectCode) && dto.SubjectCode.ToUpper() != subject.SubjectCode)
                {
                    if (await _context.Subjects.AnyAsync(s => s.SubjectCode == dto.SubjectCode.ToUpper() && s.Id != id && s.IsActive == true))
                    {
                        return BadRequest(ApiResponseDto<object>.ErrorResult("Subject code already exists"));
                    }
                    subject.SubjectCode = dto.SubjectCode.ToUpper();
                }

                // Update fields
                if (!string.IsNullOrEmpty(dto.SubjectName)) subject.SubjectName = dto.SubjectName;
                if (dto.Credits.HasValue) subject.Credits = dto.Credits.Value;
                if (!string.IsNullOrEmpty(dto.Description)) subject.Description = dto.Description;
                if (dto.IsActive.HasValue) subject.IsActive = dto.IsActive.Value;

                await _context.SaveChangesAsync();

                var response = new SubjectResponseDto
                {
                    Id = subject.Id,
                    SubjectName = subject.SubjectName,
                    SubjectCode = subject.SubjectCode,
                    Credits = subject.Credits ?? 1,
                    Description = subject.Description,
                    IsActive = subject.IsActive ?? false,
                    CreatedDate = subject.CreatedDate ?? DateTime.UtcNow
                };

                return Ok(ApiResponseDto<SubjectResponseDto>.SuccessResult(response, "Subject updated successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating subject {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Delete subject (soft delete)
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSubject(int id)
        {
            try
            {
                var subject = await _context.Subjects.FindAsync(id);
                if (subject == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Subject not found"));
                }

                // Check if subject is being used in classes
                var isUsed = await _context.ClassSubjects.AnyAsync(cs => cs.SubjectId == id && cs.IsActive == true);
                if (isUsed)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Cannot delete subject that is assigned to classes"));
                }

                // Soft delete
                subject.IsActive = false;
                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Subject deleted successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting subject {id}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get subjects assigned to a specific class
        /// </summary>
        [HttpGet("by-class/{classId}")]
        public async Task<IActionResult> GetSubjectsByClass(int classId)
        {
            try
            {
                var classExists = await _context.Classes.AnyAsync(c => c.Id == classId && c.IsActive == true);
                if (!classExists)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Class not found"));
                }

                var subjects = await _context.ClassSubjects
                    .Include(cs => cs.Subject)
                    .Include(cs => cs.Teacher)
                    .Where(cs => cs.ClassId == classId && cs.IsActive == true)
                    .Select(cs => new SubjectWithTeacherDto
                    {
                        Id = cs.Subject.Id,
                        SubjectName = cs.Subject.SubjectName,
                        SubjectCode = cs.Subject.SubjectCode,
                        Credits = cs.Subject.Credits ?? 1,
                        Description = cs.Subject.Description,
                        TeacherId = cs.TeacherId,
                        TeacherName = cs.Teacher.FullName,
                        AssignedDate = cs.CreatedDate ?? DateTime.UtcNow
                    })
                    .ToListAsync();

                return Ok(ApiResponseDto<List<SubjectWithTeacherDto>>.SuccessResult(subjects));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting subjects for class {classId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Assign subject to class with teacher
        /// </summary>
        [HttpPost("{subjectId}/assign-to-class")]
        public async Task<IActionResult> AssignSubjectToClass(int subjectId, [FromBody] AssignSubjectToClassDto dto)
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

                // Verify subject exists
                var subject = await _context.Subjects.FindAsync(subjectId);
                if (subject == null || subject.IsActive != true)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Subject not found"));
                }

                // Verify class exists
                var classEntity = await _context.Classes.FindAsync(dto.ClassId);
                if (classEntity == null || classEntity.IsActive != true)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Class not found"));
                }

                // Verify teacher exists and is a teacher
                var teacher = await _context.Users.FindAsync(dto.TeacherId);
                if (teacher == null || teacher.Role != "Teacher" || teacher.IsActive != true)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Invalid teacher selected"));
                }

                // Check if assignment already exists
                var existingAssignment = await _context.ClassSubjects
                    .FirstOrDefaultAsync(cs => cs.ClassId == dto.ClassId && cs.SubjectId == subjectId);

                if (existingAssignment != null)
                {
                    if (existingAssignment.IsActive == true)
                    {
                        return BadRequest(ApiResponseDto<object>.ErrorResult("Subject is already assigned to this class"));
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

                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Subject assigned to class successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning subject {subjectId} to class");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Remove subject from class
        /// </summary>
        [HttpDelete("{subjectId}/remove-from-class/{classId}")]
        public async Task<IActionResult> RemoveSubjectFromClass(int subjectId, int classId)
        {
            try
            {
                var classSubject = await _context.ClassSubjects
                    .FirstOrDefaultAsync(cs => cs.ClassId == classId && cs.SubjectId == subjectId && cs.IsActive == true);

                if (classSubject == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Subject assignment not found"));
                }

                // Check if there are scores for this subject in this class
                var hasScores = await _context.Scores
                    .AnyAsync(s => s.ClassId == classId && s.SubjectId == subjectId);

                if (hasScores)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Cannot remove subject that has recorded scores"));
                }

                classSubject.IsActive = false;
                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Subject removed from class successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing subject {subjectId} from class {classId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get subject statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<IActionResult> GetSubjectStatistics()
        {
            try
            {
                var totalSubjects = await _context.Subjects.CountAsync(s => s.IsActive == true);

                var byCredits = await _context.Subjects
                    .Where(s => s.IsActive == true)
                    .GroupBy(s => s.Credits)
                    .Select(g => new { Credits = g.Key ?? 1, Count = g.Count() })
                    .OrderBy(x => x.Credits)
                    .ToListAsync();

                var mostAssignedSubjects = await _context.ClassSubjects
                    .Include(cs => cs.Subject)
                    .Where(cs => cs.IsActive == true)
                    .GroupBy(cs => cs.SubjectId)
                    .Select(g => new {
                        SubjectId = g.Key,
                        SubjectName = g.First().Subject.SubjectName,
                        SubjectCode = g.First().Subject.SubjectCode,
                        AssignmentCount = g.Count()
                    })
                    .OrderByDescending(x => x.AssignmentCount)
                    .Take(5)
                    .ToListAsync();

                var result = new
                {
                    TotalSubjects = totalSubjects,
                    ByCredits = byCredits,
                    MostAssignedSubjects = mostAssignedSubjects
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subject statistics");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }
    }

    // DTOs for Subject Management
    public class CreateSubjectDto
    {
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int Credits { get; set; } = 1;
        public string? Description { get; set; }
    }

    public class UpdateSubjectDto
    {
        public string? SubjectName { get; set; }
        public string? SubjectCode { get; set; }
        public int? Credits { get; set; }
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
    }

    public class SubjectResponseDto
    {
        public int Id { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int Credits { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class SubjectWithTeacherDto : SubjectResponseDto
    {
        public int TeacherId { get; set; }
        public string TeacherName { get; set; }
        public DateTime AssignedDate { get; set; }
    }

    public class AssignSubjectToClassDto
    {
        public int ClassId { get; set; }
        public int TeacherId { get; set; }
    }
}