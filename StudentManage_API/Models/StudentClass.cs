using System;
using System.Collections.Generic;

namespace StudentManage_API.Models;

public partial class StudentClass
{
    public int Id { get; set; }

    public int StudentId { get; set; }

    public int ClassId { get; set; }

    public DateTime? EnrollDate { get; set; }

    public bool? IsActive { get; set; }

    public virtual Class Class { get; set; } = null!;

    public virtual User Student { get; set; } = null!;
}
