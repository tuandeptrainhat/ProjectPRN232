using System;
using System.Collections.Generic;

namespace StudentManage_API.Models;

public partial class VwTeachersWithSubject
{
    public int Id { get; set; }

    public string Username { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? Phone { get; set; }

    public int SubjectId { get; set; }

    public string SubjectName { get; set; } = null!;

    public string SubjectCode { get; set; } = null!;
}
