using System;
using System.Collections.Generic;

namespace StudentManage_API.Models;

public partial class Attendance
{
    public int Id { get; set; }

    public int StudentId { get; set; }

    public int ClassId { get; set; }

    public int SubjectId { get; set; }

    public DateOnly AttendanceDate { get; set; }

    public string Status { get; set; } = null!;

    public string? Note { get; set; }

    public int CreatedBy { get; set; }

    public DateTime? CreatedDate { get; set; }

    public virtual Class Class { get; set; } = null!;

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual User Student { get; set; } = null!;

    public virtual Subject Subject { get; set; } = null!;
}
