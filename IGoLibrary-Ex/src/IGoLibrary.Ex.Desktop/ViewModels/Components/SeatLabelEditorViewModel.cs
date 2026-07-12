using CommunityToolkit.Mvvm.ComponentModel;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop.ViewModels;

internal sealed partial class SeatLabelEditorViewModel : ViewModelBase
{
    public SeatLabelEditorViewModel(SeatLabelDialogRequest request)
    {
        Title = request.Title;
        Description = request.Description;
        labelText = request.InitialText ?? string.Empty;
        Validate();
    }

    public string Title { get; }

    public string Description { get; }

    [ObservableProperty]
    private string labelText;

    [ObservableProperty]
    private string validationMessage = string.Empty;

    [ObservableProperty]
    private bool canConfirm;

    partial void OnLabelTextChanged(string value) => Validate();

    public string GetNormalizedText()
    {
        return SeatLabelService.NormalizeLabelText(LabelText);
    }

    private void Validate()
    {
        try
        {
            SeatLabelService.NormalizeLabelText(LabelText);
            ValidationMessage = string.Empty;
            CanConfirm = true;
        }
        catch (ArgumentException ex)
        {
            ValidationMessage = ex.Message.Split(" (Parameter", StringSplitOptions.None)[0];
            CanConfirm = false;
        }
    }
}
