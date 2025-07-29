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
    public class StudentScoreController : ControllerBase
    {
        private readonly StudentManagementDbContext _context;
        private readonly ILogger<StudentScoreController> _logger;

        public StudentScoreController(StudentManagementDbContext context, ILogger<StudentScoreController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get student's personal score overview
        /// </summary>
        [HttpGet("overview")]
        public async Task<IActionResult> GetScoreOverview([FromQuery] int? semester = null)
        {
            try
            {
                var studentId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Get student's class information
                var studentClass = await _context.StudentClasses
                    .Include(sc => sc.Class)
                    .Include(sc => sc.Student)
                    .Where(sc => sc.StudentId == studentId && sc.IsActive == true)
                    .FirstOrDefaultAsync();

                if (studentClass == null)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Student is not enrolled in any class"));
                }

                var query = _context.Scores
                    .Include(s => s.Subject)
                    .Include(s => s.CreatedByNavigation)
                    .Where(s => s.StudentId == studentId);

                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var scores = await query
                    .OrderByDescending(s => s.CreatedDate)
                    .Select(s => new StudentPersonalScoreDto
                    {
                        Id = s.Id,
                        SubjectId = s.SubjectId,
                        SubjectName = s.Subject.SubjectName,
                        SubjectCode = s.Subject.SubjectCode,
                        ScoreType = s.ScoreType,
                        ScoreValue = s.ScoreValue,
                        MaxScore = s.MaxScore ?? 10,
                        Percentage = Math.Round(((double)s.ScoreValue / (double)(s.MaxScore ?? 10)) * 100, 1),
                        ExamDate = s.ExamDate,
                        Semester = s.Semester,
                        Note = s.Note,
                        TeacherName = s.CreatedByNavigation.FullName,
                        CreatedDate = s.CreatedDate ?? DateTime.UtcNow,
                        Grade = s.ScoreValue >= 8 ? "Excellent" :
                               s.ScoreValue >= 6.5m ? "Good" :
                               s.ScoreValue >= 5 ? "Average" : "Below Average"
                    })
                    .ToListAsync();

                // Calculate statistics
                var scoresList = scores.ToList();
                int totalScores = scoresList.Count;
                double averageScore = totalScores > 0 ? Math.Round(scoresList.Average(s => (double)s.ScoreValue), 2) : 0;
                double highestScore = totalScores > 0 ? (double)scoresList.Max(s => s.ScoreValue) : 0;
                double lowestScore = totalScores > 0 ? (double)scoresList.Min(s => s.ScoreValue) : 0;

                var overview = new StudentScoreOverviewDto
                {
                    StudentId = studentId,
                    StudentName = studentClass.Student.FullName,
                    ClassName = studentClass.Class.ClassName,
                    Grade = studentClass.Class.Grade,
                    SchoolYear = studentClass.Class.SchoolYear,
                    Semester = semester,

                    // Statistics
                    TotalScores = totalScores,
                    AverageScore = averageScore,
                    HighestScore = highestScore,
                    LowestScore = lowestScore,

                    // Grade distribution
                    ExcellentCount = scoresList.Count(s => s.ScoreValue >= 8),
                    GoodCount = scoresList.Count(s => s.ScoreValue >= 6.5m && s.ScoreValue < 8),
                    AverageCount = scoresList.Count(s => s.ScoreValue >= 5 && s.ScoreValue < 6.5m),
                    BelowAverageCount = scoresList.Count(s => s.ScoreValue < 5),

                    // Scores by subject
                    ScoresBySubject = scoresList
                        .GroupBy(s => new { s.SubjectId, s.SubjectName, s.SubjectCode })
                        .Select(g => new SubjectScoreSummaryDto
                        {
                            SubjectId = g.Key.SubjectId,
                            SubjectName = g.Key.SubjectName,
                            SubjectCode = g.Key.SubjectCode,
                            TotalScores = g.Count(),
                            AverageScore = Math.Round(g.Average(s => (double)s.ScoreValue), 2),
                            HighestScore = (double)g.Max(s => s.ScoreValue),
                            LowestScore = (double)g.Min(s => s.ScoreValue),
                            LatestScore = g.OrderByDescending(s => s.CreatedDate).First()
                        })
                        .OrderBy(s => s.SubjectName)
                        .ToList(),

                    // Recent scores
                    RecentScores = scoresList.Take(10).ToList()
                };

                return Ok(ApiResponseDto<StudentScoreOverviewDto>.SuccessResult(overview));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting student score overview");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get scores for a specific subject
        /// </summary>
        [HttpGet("subject/{subjectId}")]
        public async Task<IActionResult> GetScoresBySubject(int subjectId, [FromQuery] int? semester = null)
        {
            try
            {
                var studentId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Verify student is enrolled in a class that has this subject
                var hasSubject = await _context.StudentClasses
                    .Include(sc => sc.Class)
                        .ThenInclude(c => c.ClassSubjects)
                    .AnyAsync(sc => sc.StudentId == studentId &&
                                   sc.IsActive == true &&
                                   sc.Class.ClassSubjects.Any(cs => cs.SubjectId == subjectId && cs.IsActive == true));

                if (!hasSubject)
                {
                    return Forbid("You are not enrolled in this subject");
                }

                var query = _context.Scores
                    .Include(s => s.Subject)
                    .Include(s => s.CreatedByNavigation)
                    .Where(s => s.StudentId == studentId && s.SubjectId == subjectId);

                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var scores = await query
                    .OrderByDescending(s => s.CreatedDate)
                    .Select(s => new StudentPersonalScoreDto
                    {
                        Id = s.Id,
                        SubjectId = s.SubjectId,
                        SubjectName = s.Subject.SubjectName,
                        SubjectCode = s.Subject.SubjectCode,
                        ScoreType = s.ScoreType,
                        ScoreValue = s.ScoreValue,
                        MaxScore = s.MaxScore ?? 10,
                        Percentage = Math.Round(((double)s.ScoreValue / (double)(s.MaxScore ?? 10)) * 100, 1),
                        ExamDate = s.ExamDate,
                        Semester = s.Semester,
                        Note = s.Note,
                        TeacherName = s.CreatedByNavigation.FullName,
                        CreatedDate = s.CreatedDate ?? DateTime.UtcNow,
                        Grade = s.ScoreValue >= 8 ? "Excellent" :
                               s.ScoreValue >= 6.5m ? "Good" :
                               s.ScoreValue >= 5 ? "Average" : "Below Average"
                    })
                    .ToListAsync();

                if (!scores.Any())
                {
                    return Ok(ApiResponseDto<object>.SuccessResult(new { Message = "No scores found for this subject" }));
                }

                // Group by score type
                var scoresByType = scores
                    .GroupBy(s => s.ScoreType)
                    .Select(g => new ScoreTypeGroupDto
                    {
                        ScoreType = g.Key,
                        Count = g.Count(),
                        AverageScore = Math.Round(g.Average(s => (double)s.ScoreValue), 2),
                        HighestScore = (double)g.Max(s => s.ScoreValue),
                        LowestScore = (double)g.Min(s => s.ScoreValue),
                        Scores = g.OrderByDescending(s => s.CreatedDate).ToList()
                    })
                    .OrderBy(g => g.ScoreType)
                    .ToList();

                var result = new SubjectScoreDetailDto
                {
                    SubjectId = subjectId,
                    SubjectName = scores.First().SubjectName,
                    SubjectCode = scores.First().SubjectCode,
                    Semester = semester,
                    TotalScores = scores.Count,
                    AverageScore = Math.Round(scores.Average(s => (double)s.ScoreValue), 2),
                    HighestScore = (double)scores.Max(s => s.ScoreValue),
                    LowestScore = (double)scores.Min(s => s.ScoreValue),
                    ScoresByType = scoresByType,
                    AllScores = scores
                };

                return Ok(ApiResponseDto<SubjectScoreDetailDto>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting scores for subject {subjectId}");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get score trends over time
        /// </summary>
        [HttpGet("trends")]
        public async Task<IActionResult> GetScoreTrends([FromQuery] int? subjectId = null, [FromQuery] int months = 6)
        {
            try
            {
                var studentId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var startDate = DateTime.UtcNow.AddMonths(-months);

                var query = _context.Scores
                    .Include(s => s.Subject)
                    .Where(s => s.StudentId == studentId && s.CreatedDate >= startDate);

                if (subjectId.HasValue)
                {
                    query = query.Where(s => s.SubjectId == subjectId.Value);
                }

                var scores = await query
                    .OrderBy(s => s.CreatedDate)
                    .Select(s => new ScoreTrendDto
                    {
                        SubjectId = s.SubjectId,
                        SubjectName = s.Subject.SubjectName,
                        ScoreValue = s.ScoreValue,
                        ScoreType = s.ScoreType,
                        ExamDate = s.ExamDate,
                        CreatedDate = s.CreatedDate ?? DateTime.UtcNow,
                        MonthYear = ((DateTime)(s.CreatedDate ?? DateTime.UtcNow)).ToString("yyyy-MM")
                    })
                    .ToListAsync();

                if (!scores.Any())
                {
                    return Ok(ApiResponseDto<object>.SuccessResult(new { Message = "No scores found for the specified period" }));
                }

                // Group by month-year
                var monthlyTrends = scores
                    .GroupBy(s => s.MonthYear)
                    .Select(g => new MonthlyScoreTrendDto
                    {
                        MonthYear = g.Key,
                        Month = DateTime.ParseExact(g.Key + "-01", "yyyy-MM-dd", null).ToString("MMMM yyyy"),
                        TotalScores = g.Count(),
                        AverageScore = Math.Round(g.Average(s => (double)s.ScoreValue), 2),
                        HighestScore = g.Max(s => s.ScoreValue),
                        LowestScore = g.Min(s => s.ScoreValue),
                        ScoresBySubject = g.GroupBy(s => new { s.SubjectId, s.SubjectName })
                            .Select(sg => new
                            {
                                SubjectId = sg.Key.SubjectId,
                                SubjectName = sg.Key.SubjectName,
                                AverageScore = Math.Round(sg.Average(s => (double)s.ScoreValue), 2),
                                Count = sg.Count()
                            })
                            .Cast<object>()
                            .ToList()
                    })
                    .OrderBy(t => t.MonthYear)
                    .ToList();

                // Calculate improvement trend
                var improvementTrend = "Stable";
                if (monthlyTrends.Count >= 2)
                {
                    var firstMonthAvg = monthlyTrends.First().AverageScore;
                    var lastMonthAvg = monthlyTrends.Last().AverageScore;
                    var difference = lastMonthAvg - firstMonthAvg;

                    if (difference > 0.5) improvementTrend = "Improving";
                    else if (difference < -0.5) improvementTrend = "Declining";
                }

                var result = new
                {
                    Period = $"{months} months",
                    SubjectId = subjectId,
                    TotalScores = scores.Count,
                    OverallAverage = Math.Round(scores.Average(s => (double)s.ScoreValue), 2),
                    ImprovementTrend = improvementTrend,
                    MonthlyTrends = monthlyTrends,
                    SubjectBreakdown = subjectId == null ? scores
                        .GroupBy(s => new { s.SubjectId, s.SubjectName })
                        .Select(g => new
                        {
                            SubjectId = g.Key.SubjectId,
                            SubjectName = g.Key.SubjectName,
                            AverageScore = Math.Round(g.Average(s => (double)s.ScoreValue), 2),
                            TotalScores = g.Count(),
                            Trend = CalculateSubjectTrend(g.OrderBy(s => s.CreatedDate).ToList())
                        })
                        .OrderByDescending(s => s.AverageScore)
                        .ToList() : null
                };

                return Ok(ApiResponseDto<object>.SuccessResult(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting score trends");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get student's grade report card
        /// </summary>
        [HttpGet("report-card")]
        public async Task<IActionResult> GetReportCard([FromQuery] int semester, [FromQuery] string? schoolYear = null)
        {
            try
            {
                var studentId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                // Get student and class information
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

                // Apply school year filter if provided
                if (!string.IsNullOrEmpty(schoolYear) && studentInfo.Class.SchoolYear != schoolYear)
                {
                    return NotFound(ApiResponseDto<object>.ErrorResult("Student is not enrolled in the specified school year"));
                }

                // Get all subjects for the class
                var classSubjects = await _context.ClassSubjects
                    .Include(cs => cs.Subject)
                    .Include(cs => cs.Teacher)
                    .Where(cs => cs.ClassId == studentInfo.ClassId && cs.IsActive == true)
                    .ToListAsync();

                var reportCard = new StudentReportCardDto
                {
                    StudentId = studentId,
                    StudentName = studentInfo.Student.FullName,
                    StudentCode = studentInfo.Student.Username,
                    ClassName = studentInfo.Class.ClassName,
                    Grade = studentInfo.Class.Grade,
                    SchoolYear = studentInfo.Class.SchoolYear,
                    Semester = semester,
                    HomeroomTeacher = studentInfo.Class.Teacher?.FullName,
                    GeneratedDate = DateTime.UtcNow,

                    SubjectReports = new List<SubjectReportDto>()
                };

                decimal totalWeightedScore = 0;
                int totalCredits = 0;

                foreach (var classSubject in classSubjects)
                {
                    var subjectScores = await _context.Scores
                        .Where(s => s.StudentId == studentId &&
                                   s.SubjectId == classSubject.SubjectId &&
                                   s.Semester == semester)
                        .ToListAsync();

                    var subjectReport = new SubjectReportDto
                    {
                        SubjectId = classSubject.SubjectId,
                        SubjectName = classSubject.Subject.SubjectName,
                        SubjectCode = classSubject.Subject.SubjectCode,
                        Credits = classSubject.Subject.Credits ?? 1,
                        TeacherName = classSubject.Teacher.FullName,
                        TotalScores = subjectScores.Count
                    };

                    if (subjectScores.Any())
                    {
                        // Calculate different score components
                        var midtermScores = subjectScores.Where(s => s.ScoreType == "Midterm").ToList();
                        var finalScores = subjectScores.Where(s => s.ScoreType == "Final").ToList();
                        var assignmentScores = subjectScores.Where(s => s.ScoreType == "Assignment").ToList();
                        var quizScores = subjectScores.Where(s => s.ScoreType == "Quiz").ToList();

                        subjectReport.MidtermAverage = midtermScores.Any() ?
                            Math.Round(midtermScores.Average(s => (double)s.ScoreValue), 2) : null;
                        subjectReport.FinalAverage = finalScores.Any() ?
                            Math.Round(finalScores.Average(s => (double)s.ScoreValue), 2) : null;
                        subjectReport.AssignmentAverage = assignmentScores.Any() ?
                            Math.Round(assignmentScores.Average(s => (double)s.ScoreValue), 2) : null;
                        subjectReport.QuizAverage = quizScores.Any() ?
                            Math.Round(quizScores.Average(s => (double)s.ScoreValue), 2) : null;

                        // Calculate overall subject average (weighted)
                        double subjectAverage = 0;
                        double totalWeight = 0;

                        if (subjectReport.MidtermAverage.HasValue)
                        {
                            subjectAverage += subjectReport.MidtermAverage.Value * 0.3; // 30% weight
                            totalWeight += 0.3;
                        }
                        if (subjectReport.FinalAverage.HasValue)
                        {
                            subjectAverage += subjectReport.FinalAverage.Value * 0.4; // 40% weight
                            totalWeight += 0.4;
                        }
                        if (subjectReport.AssignmentAverage.HasValue)
                        {
                            subjectAverage += subjectReport.AssignmentAverage.Value * 0.2; // 20% weight
                            totalWeight += 0.2;
                        }
                        if (subjectReport.QuizAverage.HasValue)
                        {
                            subjectAverage += subjectReport.QuizAverage.Value * 0.1; // 10% weight
                            totalWeight += 0.1;
                        }

                        if (totalWeight > 0)
                        {
                            subjectReport.SubjectAverage = Math.Round(subjectAverage / totalWeight, 2);
                            subjectReport.LetterGrade = CalculateLetterGrade(subjectReport.SubjectAverage);

                            // Add to GPA calculation
                            totalWeightedScore += (decimal)subjectReport.SubjectAverage * subjectReport.Credits;
                            totalCredits += subjectReport.Credits;
                        }
                        else
                        {
                            subjectReport.SubjectAverage = Math.Round(subjectScores.Average(s => (double)s.ScoreValue), 2);
                            subjectReport.LetterGrade = CalculateLetterGrade(subjectReport.SubjectAverage);
                        }
                    }
                    else
                    {
                        subjectReport.LetterGrade = "N/A";
                        subjectReport.Comments = "No scores recorded";
                    }

                    reportCard.SubjectReports.Add(subjectReport);
                }

                // Calculate overall GPA
                reportCard.OverallGPA = totalCredits > 0 ? Math.Round((double)(totalWeightedScore / totalCredits), 2) : 0;
                reportCard.OverallLetterGrade = CalculateLetterGrade(reportCard.OverallGPA);
                reportCard.TotalCredits = totalCredits;

                // Calculate class ranking (optional - requires all students' scores)
                reportCard.ClassRanking = await CalculateClassRanking(studentId, studentInfo.ClassId, semester);

                return Ok(ApiResponseDto<StudentReportCardDto>.SuccessResult(reportCard));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating report card");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        /// <summary>
        /// Get comparison with class average
        /// </summary>
        [HttpGet("class-comparison")]
        public async Task<IActionResult> GetClassComparison([FromQuery] int? semester = null)
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

                // Get all students in the same class
                var classmates = await _context.StudentClasses
                    .Where(sc => sc.ClassId == studentClass.ClassId && sc.IsActive == true)
                    .Select(sc => sc.StudentId)
                    .ToListAsync();

                var query = _context.Scores
                    .Include(s => s.Subject)
                    .Where(s => classmates.Contains(s.StudentId));

                if (semester.HasValue)
                {
                    query = query.Where(s => s.Semester == semester.Value);
                }

                var allScores = await query.ToListAsync();
                var studentScores = allScores.Where(s => s.StudentId == studentId).ToList();

                var comparison = new ClassComparisonDto
                {
                    StudentId = studentId,
                    ClassId = studentClass.ClassId,
                    ClassName = studentClass.Class.ClassName,
                    Semester = semester,
                    TotalClassmates = classmates.Count,

                    SubjectComparisons = allScores
                        .GroupBy(s => new { s.SubjectId, s.Subject.SubjectName })
                        .Select(g => new SubjectComparisonDto
                        {
                            SubjectId = g.Key.SubjectId,
                            SubjectName = g.Key.SubjectName,
                            StudentAverage = studentScores.Where(s => s.SubjectId == g.Key.SubjectId).Any() ?
                                Math.Round(studentScores.Where(s => s.SubjectId == g.Key.SubjectId).Average(s => (double)s.ScoreValue), 2) : 0,
                            ClassAverage = Math.Round(g.Average(s => (double)s.ScoreValue), 2),
                            ClassHighest = (double)g.Max(s => s.ScoreValue),
                            ClassLowest = (double)g.Min(s => s.ScoreValue),
                            StudentRanking = CalculateSubjectRanking(studentId, g.ToList()),
                            TotalStudentsWithScores = g.Select(s => s.StudentId).Distinct().Count()
                        })
                        .Where(c => c.StudentAverage > 0) // Only show subjects where student has scores
                        .OrderByDescending(c => c.StudentAverage)
                        .ToList()
                };

                // Calculate overall comparison
                if (comparison.SubjectComparisons.Any())
                {
                    comparison.OverallStudentAverage = Math.Round(comparison.SubjectComparisons.Average(c => c.StudentAverage), 2);
                    comparison.OverallClassAverage = Math.Round(comparison.SubjectComparisons.Average(c => c.ClassAverage), 2);
                    comparison.PerformanceStatus = comparison.OverallStudentAverage > comparison.OverallClassAverage ? "Above Average" :
                                                  comparison.OverallStudentAverage == comparison.OverallClassAverage ? "Average" : "Below Average";
                }

                return Ok(ApiResponseDto<ClassComparisonDto>.SuccessResult(comparison));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting class comparison");
                return StatusCode(500, ApiResponseDto<object>.ErrorResult("Internal server error"));
            }
        }

        // Helper methods
        private static string CalculateLetterGrade(double score)
        {
            return score switch
            {
                >= 9.0 => "A+",
                >= 8.5 => "A",
                >= 8.0 => "A-",
                >= 7.5 => "B+",
                >= 7.0 => "B",
                >= 6.5 => "B-",
                >= 6.0 => "C+",
                >= 5.5 => "C",
                >= 5.0 => "C-",
                >= 4.0 => "D",
                _ => "F"
            };
        }

        private static string CalculateSubjectTrend(List<ScoreTrendDto> scores)
        {
            if (scores.Count < 2) return "Insufficient data";

            var firstScore = scores.First().ScoreValue;
            var lastScore = scores.Last().ScoreValue;
            var difference = lastScore - firstScore;

            return difference switch
            {
                > 0.5m => "Improving",
                < -0.5m => "Declining",
                _ => "Stable"
            };
        }

        private static int CalculateSubjectRanking(int studentId, List<Score> subjectScores)
        {
            var studentAverage = subjectScores.Where(s => s.StudentId == studentId).Average(s => (double)s.ScoreValue);
            var studentAverages = subjectScores
                .GroupBy(s => s.StudentId)
                .Select(g => g.Average(s => (double)s.ScoreValue))
                .OrderByDescending(avg => avg)
                .ToList();

            return studentAverages.IndexOf(studentAverage) + 1;
        }

        private async Task<int> CalculateClassRanking(int studentId, int classId, int semester)
        {
            try
            {
                var classmates = await _context.StudentClasses
                    .Where(sc => sc.ClassId == classId && sc.IsActive == true)
                    .Select(sc => sc.StudentId)
                    .ToListAsync();

                var classmateAverages = new List<(int StudentId, double Average)>();

                foreach (var classmateId in classmates)
                {
                    var scores = await _context.Scores
                        .Where(s => s.StudentId == classmateId && s.Semester == semester)
                        .ToListAsync();

                    if (scores.Any())
                    {
                        var average = scores.Average(s => (double)s.ScoreValue);
                        classmateAverages.Add((classmateId, average));
                    }
                }

                var rankedStudents = classmateAverages
                    .OrderByDescending(ca => ca.Average)
                    .ToList();

                var studentRank = rankedStudents.FindIndex(rs => rs.StudentId == studentId) + 1;
                return studentRank > 0 ? studentRank : rankedStudents.Count + 1;
            }
            catch
            {
                return 0; // Return 0 if ranking calculation fails
            }
        }
    }

    // DTOs for Student Score Management
    public class StudentPersonalScoreDto
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public string ScoreType { get; set; }
        public decimal ScoreValue { get; set; }
        public decimal MaxScore { get; set; }
        public double Percentage { get; set; }
        public DateOnly? ExamDate { get; set; }
        public int? Semester { get; set; }
        public string? Note { get; set; }
        public string TeacherName { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Grade { get; set; }
    }

    public class StudentScoreOverviewDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public string SchoolYear { get; set; }
        public int? Semester { get; set; }

        public int TotalScores { get; set; }
        public double AverageScore { get; set; }
        public double HighestScore { get; set; }
        public double LowestScore { get; set; }

        public int ExcellentCount { get; set; }
        public int GoodCount { get; set; }
        public int AverageCount { get; set; }
        public int BelowAverageCount { get; set; }

        public List<SubjectScoreSummaryDto> ScoresBySubject { get; set; } = new();
        public List<StudentPersonalScoreDto> RecentScores { get; set; } = new();
    }

    public class SubjectScoreSummaryDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int TotalScores { get; set; }
        public double AverageScore { get; set; }
        public double HighestScore { get; set; }
        public double LowestScore { get; set; }
        public StudentPersonalScoreDto LatestScore { get; set; }
    }

    public class SubjectScoreDetailDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int? Semester { get; set; }
        public int TotalScores { get; set; }
        public double AverageScore { get; set; }
        public double HighestScore { get; set; }
        public double LowestScore { get; set; }
        public List<ScoreTypeGroupDto> ScoresByType { get; set; } = new();
        public List<StudentPersonalScoreDto> AllScores { get; set; } = new();
    }

    public class ScoreTypeGroupDto
    {
        public string ScoreType { get; set; }
        public int Count { get; set; }
        public double AverageScore { get; set; }
        public double HighestScore { get; set; }
        public double LowestScore { get; set; }
        public List<StudentPersonalScoreDto> Scores { get; set; } = new();
    }

    public class ScoreTrendDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public decimal ScoreValue { get; set; }
        public string ScoreType { get; set; }
        public DateOnly? ExamDate { get; set; }
        public DateTime CreatedDate { get; set; }
        public string MonthYear { get; set; }
    }

    public class MonthlyScoreTrendDto
    {
        public string MonthYear { get; set; }
        public string Month { get; set; }
        public int TotalScores { get; set; }
        public double AverageScore { get; set; }
        public decimal HighestScore { get; set; }
        public decimal LowestScore { get; set; }
        public List<object> ScoresBySubject { get; set; } = new();
    }

    public class StudentReportCardDto
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public string StudentCode { get; set; }
        public string ClassName { get; set; }
        public int Grade { get; set; }
        public string SchoolYear { get; set; }
        public int Semester { get; set; }
        public string? HomeroomTeacher { get; set; }
        public DateTime GeneratedDate { get; set; }
        public double OverallGPA { get; set; }
        public string OverallLetterGrade { get; set; }
        public int TotalCredits { get; set; }
        public int ClassRanking { get; set; }
        public List<SubjectReportDto> SubjectReports { get; set; } = new();
    }

    public class SubjectReportDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int Credits { get; set; }
        public string TeacherName { get; set; }
        public int TotalScores { get; set; }
        public double? MidtermAverage { get; set; }
        public double? FinalAverage { get; set; }
        public double? AssignmentAverage { get; set; }
        public double? QuizAverage { get; set; }
        public double SubjectAverage { get; set; }
        public string LetterGrade { get; set; }
        public string? Comments { get; set; }
    }

    public class ClassComparisonDto
    {
        public int StudentId { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; }
        public int? Semester { get; set; }
        public int TotalClassmates { get; set; }
        public double OverallStudentAverage { get; set; }
        public double OverallClassAverage { get; set; }
        public string PerformanceStatus { get; set; }
        public List<SubjectComparisonDto> SubjectComparisons { get; set; } = new();
    }

    public class SubjectComparisonDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public double StudentAverage { get; set; }
        public double ClassAverage { get; set; }
        public double ClassHighest { get; set; }
        public double ClassLowest { get; set; }
        public int StudentRanking { get; set; }
        public int TotalStudentsWithScores { get; set; }
    }
}