using System;
using System.Collections.Generic;

namespace StudentManage_API.Models;

public partial class VwStudentsWithClass
{
    public int Id { get; set; }

    public string Username { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? Phone { get; set; }

    public int ClassId { get; set; }

    public string ClassName { get; set; } = null!;

    public int Grade { get; set; }

    public DateTime? EnrollDate { get; set; }

    public bool? IsEnrolled { get; set; }
}
