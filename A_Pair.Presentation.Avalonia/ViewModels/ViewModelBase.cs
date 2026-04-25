using CommunityToolkit.Mvvm.ComponentModel;

namespace A_Pair.Presentation.Avalonia.ViewModels
{
    public abstract class ViewModelBase : ObservableObject
    {
        // common properties
        public bool IsBusy { get; set; }
    }
}
