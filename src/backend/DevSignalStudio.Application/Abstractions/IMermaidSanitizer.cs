using DevSignalStudio.Application.Models;

namespace DevSignalStudio.Application.Abstractions;

public interface IMermaidSanitizer
{
    MermaidSanitizationResult Sanitize(string? source);
}
