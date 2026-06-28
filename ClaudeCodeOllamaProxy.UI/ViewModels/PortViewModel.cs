using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeCodeOllamaProxy.UI.ViewModels;

/// <summary>
/// Validates the port input. Uses CommunityToolkit.Mvvm's <see cref="ObservableValidator"/> +
/// data-annotation attributes so invalid entries (non-numeric or out of range) are reported via
/// <see cref="ObservableValidator.HasErrors"/> / <see cref="FirstError"/>.
/// </summary>
public sealed partial class PortViewModel : ObservableValidator
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "Enter a port between 1 and 65535.")]
    public partial double Port { get; set; }

    /// <summary>The first validation error message for <see cref="Port"/>, or null when valid.</summary>
    public string? FirstError => GetErrors(nameof(Port)).FirstOrDefault()?.ErrorMessage;
}
