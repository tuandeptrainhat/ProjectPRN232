using System;
using System.Collections.Generic;

namespace StudentManage_API.Models;

public partial class Score
{
    public int Id { get; set; }

    public int StudentId { get; set; }

    public int SubjectId { get; set; }

    public int ClassId { get; set; }

    public string ScoreType { get; set; } = null!;

    public decimal ScoreValue { get; set; }

    public decimal? MaxScore { get; set; }

    public DateOnly? ExamDate { get; set; }

    public int? Semester { get; set; }

    public string? Note { get; set; }

    public int CreatedBy { get; set; }

    public DateTime? CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public virtual Class Class { get; set; } = null!;

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual User Student { get; set; } = null!;

    public virtual Subject Subject { get; set; } = null!;
}
