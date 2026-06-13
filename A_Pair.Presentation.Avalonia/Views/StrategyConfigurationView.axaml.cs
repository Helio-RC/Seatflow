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
        /// 子策略项（依赖策略）被点击时，在 PointerPressed 阶段抢先拦截，
        /// 触发 ViewModel 中的 SelectSelfCommand 并阻止冒泡到父 ListBox。
        /// 使用 PointerPressed 而非 Tapped 是因为 ListBox 在 PointerPressed 时选中项，
        /// Tapped 在 PointerReleased 后才触发，为时已晚。
        /// </summary>
        private void OnChildStrategyPressed (object? sender , PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is StrategyItemViewModel child)
            {
                child.SelectSelfCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
