using StudentManage_API.Models;
using System;
using System.Collections.Generic;

namespace StudentManage_API;

public partial class Notification
{
    public int Id { get; set; }

    public string Title { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string? TargetRole { get; set; }

    public int? ClassId { get; set; }

    public int? UserId { get; set; }

    public string? Priority { get; set; }

    public int CreatedBy { get; set; }

    public DateTime? CreatedDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public bool? IsActive { get; set; }

    public virtual Class? Class { get; set; }

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual User? User { get; set; }
}
