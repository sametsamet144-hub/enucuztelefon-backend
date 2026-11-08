using EnUcuzTelefon.Domain.Common;
using EnUcuzTelefon.Domain.Enums;

namespace EnUcuzTelefon.Domain.Entities;

public class PriceAlert : BaseEntity, IAggregateRoot
{
    public string? UserId { get; set; }
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;

    public decimal TargetPrice { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsTriggered { get; set; }
    public DateTime? TriggeredAt { get; set; }
    public decimal? TriggeredPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public int NotificationCount { get; set; }
}

public class Favorite : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
}

public class ScrapingJob : BaseEntity, IAggregateRoot
{
    public Guid StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public string JobType { get; set; } = "ScheduledTwoHourSync";
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? HeartbeatAt { get; set; }
    public int ItemsProcessed { get; set; }
    public int ItemsUpdated { get; set; }
    public int ItemsFailed { get; set; }
    public string? ErrorMessage { get; set; }

    public ICollection<JobLog> Logs { get; set; } = new List<JobLog>();
}

public class JobLog : BaseEntity
{
    public Guid ScrapingJobId { get; set; }
    public ScrapingJob ScrapingJob { get; set; } = null!;

    public string Level { get; set; } = "Information";
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class PriceAnomaly : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = null!;
    public Guid VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;
    public Guid StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public double DropPercent { get; set; }
    public string Reason { get; set; } = string.Empty; // e.g. "Extreme price drop > 40%"
    public AnomalyStatus Status { get; set; } = AnomalyStatus.PendingReview;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByUserId { get; set; }
}

public class DataValidationLog : BaseEntity
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning"; // Warning, Error, Info
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AuditLog : BaseEntity
{
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? ChangesJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class Article : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string Category { get; set; } = "Rehber";
    public List<string> Tags { get; set; } = new();
    public int ReadingTimeMinutes { get; set; } = 5;
    public string? Author { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
}

public class QAItem : BaseEntity
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Category { get; set; } = "Genel";
    public List<string> Tags { get; set; } = new();
    public int HelpfulCount { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
}
