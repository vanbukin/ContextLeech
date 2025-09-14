using System.ComponentModel.DataAnnotations;

namespace ContextLeech.Configuration;

public class ApplicationOptions
{
    [Required]
    [MaxLength(10000)]
    public string RepoPath { get; set; } = default!;
}
