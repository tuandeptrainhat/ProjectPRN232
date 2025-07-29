using System;
using System.Collections.Generic;

namespace StudentManage_API.Models;

public partial class StudentParent
{
    public int Id { get; set; }

    public int StudentId { get; set; }

    public int ParentId { get; set; }

    public string Relationship { get; set; } = null!;

    public bool? IsEmergencyContact { get; set; }

    public virtual Parent Parent { get; set; } = null!;

    public virtual User Student { get; set; } = null!;
}
