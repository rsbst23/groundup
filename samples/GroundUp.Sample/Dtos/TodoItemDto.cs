namespace GroundUp.Sample.Dtos;

/// <summary>
/// DTO for TodoItem — used for both input and output in the simple case.
/// Audit fields (CreatedAt, UpdatedAt) are handled by the framework automatically
/// and returned in responses but not accepted as input.
/// </summary>
public class TodoItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsComplete { get; set; }
    public DateTime? DueDate { get; set; }
}
