using System;
using System.Collections.Generic;

namespace StudentManage_API.Models;

public partial class Parent
{
    public int Id { get; set; }

    public string FullName { get; set; } = null!;

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? Occupation { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedDate { get; set; }

    public virtual ICollection<StudentParent> StudentParents { get; set; } = new List<StudentParent>();
}
