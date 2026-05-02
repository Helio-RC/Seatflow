using A_Pair.Application.Interfaces;
using A_Pair.Presentation.Avalonia.Services;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class VenueConfigurationViewModel : ViewModelBase
{
    private readonly IApplicationFacade _facade;
    private readonly IFileService _fileService;

    public string Title { get; } = "会场配置";

    public VenueConfigurationViewModel(IApplicationFacade facade, IFileService fileService, IDialogService dialog)
    {
        _facade = facade;
        _fileService = fileService;
    }
}
