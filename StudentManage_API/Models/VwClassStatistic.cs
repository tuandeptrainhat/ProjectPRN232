using System;
using System.Collections.Generic;

namespace StudentManage_API.Models;

public partial class VwClassStatistic
{
    public int Id { get; set; }

    public string ClassName { get; set; } = null!;

    public int Grade { get; set; }

    public int? MaxStudents { get; set; }

    public int? CurrentStudents { get; set; }

    public string? TeacherName { get; set; }
}
