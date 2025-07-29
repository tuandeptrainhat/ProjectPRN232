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
    public class TeacherScoreController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<TeacherScoreController> _logger;

        public TeacherScoreController(StudentManagementDbContext context, ILogger<TeacherScoreController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get classes that teacher is assigned to teach
        /// </summary>
        [HttpGet("my-classes")]
        public async Task<IActionResult> GetMyClasses()
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                var classes = await _context.ClassSubjects
                    .Include(cs => cs.Class)
                    .Include(cs => cs.Subject)
                    .Where(cs => cs.TeacherId == teacherId && cs.IsActive == true)
                    .Select(cs => new TeacherClassDto
                    {
                        ClassId = cs.ClassId,
                        ClassName = cs.Class.ClassName,
                        Grade = cs.Class.Grade,
                        SchoolYear = cs.Class.SchoolYear,
                        SubjectId = cs.SubjectId,
                        SubjectName = cs.Subject.SubjectName,
                        SubjectCode = cs.Subject.SubjectCode,
                        Credits = cs.Subject.Credits ?? 1,
                        StudentCount = cs.Class.StudentClasses.Count(sc => sc.IsActive == true)
                    })
                    .OrderBy(tc => tc.ClassName)
                    .ThenBy(tc => tc.SubjectName)
                    .ToListAsync();

                return Ok(ApiResponseDto<List<TeacherClassDto>>.SuccessResult(classes));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting teacher classes");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get students in a specific class for a specific subject
        /// </summary>
        [HttpGet("class/{classId}/subject/{subjectId}/students")]
        public async Task<IActionResult> GetStudentsForScoring(int classId, int subjectId)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Verify teacher has permission to this class-subject
                var hasPermission = await _context.ClassSubjects
                    .AnyAsync(cs => cs.ClassId == classId && cs.SubjectId == subjectId &&
                                   cs.TeacherId == teacherId && cs.IsActive == true);

                if (!hasPermission)
                {
                    return Forbid("You don't have permission to manage scores for this class-subject combination");
                }

                var students = await _context.StudentClasses
                    .Include(sc => sc.Student)
                    .Where(sc => sc.ClassId == classId && sc.IsActive == true)
                    .Select(sc => new StudentForScoringDto
                    {
                        StudentId = sc.StudentId,
                        StudentName = sc.Student.FullName,
                        StudentEmail = sc.Student.Email,
                        EnrollDate = sc.EnrollDate ?? DateTime.UtcNow,
                        // Get existing scores for this subject
                        Scores = _context.Scores
                            .Where(s => s.StudentId == sc.StudentId && s.SubjectId == subjectId && s.ClassId == classId)
                            .Select(s => new StudentScoreDto
                            {
                                Id = s.Id,
                                ScoreType = s.ScoreType,
                                ScoreValue = s.ScoreValue,
                                MaxScore = s.MaxScore ?? 10,
                                ExamDate = s.ExamDate,
                                Semester = s.Semester,
                                Note = s.Note,
                                CreatedDate = s.CreatedDate ?? DateTime.UtcNow,
                                UpdatedDate = s.UpdatedDate ?? DateTime.UtcNow
                            })
                            .OrderByDescending(s => s.CreatedDate)
                            .ToList()
                    })
                    .OrderBy(s => s.StudentName)
                    .ToListAsync();

                return Ok(ApiResponseDto<List<StudentForScoringDto>>.SuccessResult(students));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting students for class {classId}, subject {subjectId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Add or update score for a student
        /// </summary>
        [HttpPost("add-score")]
        public async Task<IActionResult> AddScore([FromBody] AddScoreDto dto)
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
                    return Forbid("You don't have permission to add scores for this class-subject combination");
                }

                // Verify student is in the class
                var studentInClass = await _context.StudentClasses
                    .AnyAsync(sc => sc.StudentId == dto.StudentId && sc.ClassId == dto.ClassId && sc.IsActive == true);

                if (!studentInClass)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult("Student is not enrolled in this class"));
                }

                // Check if score already exists for this combination
                var existingScore = await _context.Scores
                    .FirstOrDefaultAsync(s => s.StudentId == dto.StudentId &&
                                            s.SubjectId == dto.SubjectId &&
                                            s.ClassId == dto.ClassId &&
                                            s.ScoreType == dto.ScoreType &&
                                            s.Semester == dto.Semester);

                if (existingScore != null)
                {
                    return BadRequest(ApiResponseDto<object>.ErrorResult(
                        $"Score for {dto.ScoreType} in semester {dto.Semester} already exists. Use update instead."));
                }

                var score = new Score
                {
                    StudentId = dto.StudentId,
                    SubjectId = dto.SubjectId,
                    ClassId = dto.ClassId,
                    ScoreType = dto.ScoreType,
                    ScoreValue = dto.ScoreValue,
                    MaxScore = dto.MaxScore,
                    ExamDate = dto.ExamDate,
                    Semester = dto.Semester,
                    Note = dto.Note,
                    CreatedBy = teacherId,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                };

                _context.Scores.Add(score);
                await _context.SaveChangesAsync();

                var response = new StudentScoreDto
                {
                    Id = score.Id,
                    ScoreType = score.ScoreType,
                    ScoreValue = score.ScoreValue,
                    MaxScore = score.MaxScore ?? 10,
                    ExamDate = score.ExamDate,
                    Semester = score.Semester,
                    Note = score.Note,
                    CreatedDate = score.CreatedDate ?? DateTime.UtcNow,
                    UpdatedDate = score.UpdatedDate ?? DateTime.UtcNow
                };

                return Ok(ApiResponseDto<StudentScoreDto>.SuccessResult(response, "Score added successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding score");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Update existing score
        /// </summary>
        [HttpPut("update-score/{scoreId}")]
        public async Task<IActionResult> UpdateScore(int scoreId, [FromBody] UpdateScoreDto dto)
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

                var score = await _context.Scores
                    .Include(s => s.CreatedByNavigation)
                    .FirstOrDefaultAsync(s => s.Id == scoreId);

                if (score == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Score not found"));
                }

                // Verify teacher has permission (either created the score or teaches the subject)
                var hasPermission = score.CreatedBy == teacherId ||
                    await _context.ClassSubjects.AnyAsync(cs => cs.ClassId == score.ClassId &&
                                                              cs.SubjectId == score.SubjectId &&
                                                              cs.TeacherId == teacherId &&
                                                              cs.IsActive == true);

                if (!hasPermission)
                {
                    return Forbid("You don't have permission to update this score");
                }

                // Update score
                if (dto.ScoreValue.HasValue) score.ScoreValue = dto.ScoreValue.Value;
                if (dto.MaxScore.HasValue) score.MaxScore = dto.MaxScore.Value;
                if (dto.ExamDate.HasValue) score.ExamDate = dto.ExamDate;
                if (!string.IsNullOrEmpty(dto.Note)) score.Note = dto.Note;

                score.UpdatedDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Score updated successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating score {scoreId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Delete score
        /// </summary>
        [HttpDelete("delete-score/{scoreId}")]
        public async Task<IActionResult> DeleteScore(int scoreId)
        {
            try
            {
                var teacherId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                var score = await _context.Scores.FindAsync(scoreId);
                if (score == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Score not found"));
                }

                // Verify teacher has permission
                var hasPermission = score.CreatedBy == teacherId ||
                    await _context.ClassSubjects.AnyAsync(cs => cs.ClassId == score.ClassId &&
                                                              cs.SubjectId == score.SubjectId &&
                                                              cs.TeacherId == teacherId &&
                                                              cs.IsActive == true);

                if (!hasPermission)
                {
                    return Forbid("You don't have permission to delete this score");
                }

                _context.Scores.Remove(score);
                await _context.SaveChangesAsync();

                return Ok(ApiResponseDto<object>.SuccessResult(null, "Score deleted successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting score {scoreId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Add multiple scores at once (batch input)
        /// </summary>
        [HttpPost("batch-add-scores")]
        public async Task<IActionResult> BatchAddScores([FromBody] BatchAddScoresDto dto)
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
                    return Forbid("You don't have permission to add scores for this class-subject combination");
                }

                var successfulScores = new List<StudentScoreDto>();
                var failedScores = new List<string>();

                foreach (var scoreData in dto.Scores)
                {
                    try
                    {
                        // Verify student is in class
                        var studentInClass = await _context.StudentClasses
                            .AnyAsync(sc => sc.StudentId == scoreData.StudentId && sc.ClassId == dto.ClassId && sc.IsActive == true);

                        if (!studentInClass)
                        {
                            failedScores.Add($"Student ID {scoreData.StudentId}: Not enrolled in this class");
                            continue;
                        }

                        // Check for existing score
                        var existingScore = await _context.Scores
                            .AnyAsync(s => s.StudentId == scoreData.StudentId &&
                                          s.SubjectId == dto.SubjectId &&
                                          s.ClassId == dto.ClassId &&
                                          s.ScoreType == dto.ScoreType &&
                                          s.Semester == dto.Semester);

                        if (existingScore)
                        {
                            failedScores.Add($"Student ID {scoreData.StudentId}: Score already exists");
                            continue;
                        }

                        var score = new Score
                        {
                            StudentId = scoreData.StudentId,
                            SubjectId = dto.SubjectId,
                            ClassId = dto.ClassId,
                            ScoreType = dto.ScoreType,
                            ScoreValue = scoreData.ScoreValue,
                            MaxScore = dto.MaxScore,
                            ExamDate = dto.ExamDate,
                            Semester = dto.Semester,
                            Note = scoreData.Note,
                            CreatedBy = teacherId,
                            CreatedDate = DateTime.UtcNow,
                            UpdatedDate = DateTime.UtcNow
                        };

                        _context.Scores.Add(score);
                        await _context.SaveChangesAsync();

                        successfulScores.Add(new StudentScoreDto
                        {
                            Id = score.Id,
                            ScoreType = score.ScoreType,
                            ScoreValue = score.ScoreValue,
                            MaxScore = score.MaxScore ?? 10,
                            ExamDate = score.ExamDate,
                            Semester = score.Semester,
                            Note = score.Note,
                            CreatedDate = score.CreatedDate ?? DateTime.UtcNow,
                            UpdatedDate = score.UpdatedDate ?? DateTime.UtcNow
                        });
                    }
                    catch (Exception ex)
                    {
                        failedScores.Add($"Student ID {scoreData.StudentId}: {ex.Message}");
                    }
                }

                var result = new
                {
                    SuccessfulScores = successfulScores,
                    FailedScores = failedScores,
                    SuccessCount = successfulScores.Count,
                    FailureCount = failedScores.Count
                };

                var message = $"Batch operation completed: {successfulScores.Count} successful, {failedScores.Count} failed";
                return Ok(ApiResponseDto<object>.SuccessResult(result, message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch add scores");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get score statistics for a class-subject
        /// </summary>
        [HttpGet("class/{classId}/subject/{subjectId}/statistics")]
        public async Task<IActionResult> GetScoreStatistics(int classId, int subjectId, [FromQuery] int? semester = null)
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
                    return Forbid("You don't have permission to view statistics for this class-subject combination");
                }

                var query = _context.Scores
                    .Where(s => s.ClassId == classId && s.SubjectId == subjectId);

                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var scores = await query.ToListAsync();

                if (!scores.Any())
                {
                    return Ok(ApiResponseDto<object>.SuccessResult(new { Message = "No scores found" }));
                }

                var statistics = new
                {
                    TotalScores = scores.Count,
                    AverageScore = Math.Round(scores.Average(s => (double)s.ScoreValue), 2),
                    HighestScore = scores.Max(s => s.ScoreValue),
                    LowestScore = scores.Min(s => s.ScoreValue),
                    PassingRate = Math.Round((double)scores.Count(s => s.ScoreValue >= 5) / scores.Count * 100, 2),
                    ScoreDistribution = scores
                        .GroupBy(s => s.ScoreValue >= 8 ? "Excellent (8-10)" :
                                     s.ScoreValue >= 6.5m ? "Good (6.5-7.9)" :
                                     s.ScoreValue >= 5 ? "Average (5-6.4)" :
                                     "Below Average (<5)")
                        .Select(g => new { Range = g.Key, Count = g.Count() })
                        .ToList(),
                    ByScoreType = scores
                        .GroupBy(s => s.ScoreType)
                        .Select(g => new {
                            ScoreType = g.Key,
                            Count = g.Count(),
                            Average = Math.Round(g.Average(s => (double)s.ScoreValue), 2)
                        })
                        .ToList()
                };

                return Ok(ApiResponseDto<object>.SuccessResult(statistics));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting score statistics for class {classId}, subject {subjectId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }
    }

    // DTOs for Teacher Score Management
    public class TeacherClassDto
    {
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public string SchoolYear { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int Credits { get; set; }
        public int StudentCount { get; set; }
    }

    public class StudentForScoringDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public string StudentEmail { get; set; }
        public DateTime EnrollDate { get; set; }
        public List<StudentScoreDto> Scores { get; set; } = new();
    }

    public class StudentScoreDto
    {
        public int Id { get; set; }
        public string ScoreType { get; set; }
        public decimal ScoreValue { get; set; }
        public decimal MaxScore { get; set; }
        public DateOnly? ExamDate { get; set; }
        public int? Semester { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
    }

    public class AddScoreDto
    {
        public int StudentId { get; set; }
        public int SubjectId { get; set; }
        public int ClassId { get; set; }
        public string ScoreType { get; set; } // Midterm, Final, Assignment, Quiz
        public decimal ScoreValue { get; set; }
        public decimal MaxScore { get; set; } = 10;
        public DateOnly? ExamDate { get; set; }
        public int? Semester { get; set; }
        public string? Note { get; set; }
    }

    public class UpdateScoreDto
    {
        public decimal? ScoreValue { get; set; }
        public decimal? MaxScore { get; set; }
        public DateOnly? ExamDate { get; set; }
        public string? Note { get; set; }
    }

    public class BatchScoreDataDto
    {
        public int StudentId { get; set; }
        public decimal ScoreValue { get; set; }
        public string? Note { get; set; }
    }

    public class BatchAddScoresDto
    {
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public string ScoreType { get; set; }
        public decimal MaxScore { get; set; } = 10;
        public DateOnly? ExamDate { get; set; }
        public int? Semester { get; set; }
        public List<BatchScoreDataDto> Scores { get; set; } = new();
    }
}