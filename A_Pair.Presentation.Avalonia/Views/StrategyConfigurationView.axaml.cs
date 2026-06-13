using Avalonia.Controls;
using Avalonia.Input;
using A_Pair.Presentation.Avalonia.ViewModels;

namespace A_Pair.Presentation.Avalonia.Views
{
    public partial class StrategyConfigurationView : UserControl
    {
        public StrategyConfigurationView ()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 子策略项（依赖策略）被点击时，触发 ViewModel 中的 SelectSelfCommand，
        /// 避免事件冒泡到父 ListBox 导致选中宿主策略。
        /// </summary>
        private void OnChildStrategyTapped (object? sender , TappedEventArgs e)
        {
            if (sender is Border border && border.DataContext is StrategyItemViewModel child)
            {
                child.SelectSelfCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
