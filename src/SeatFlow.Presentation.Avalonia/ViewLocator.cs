using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SeatFlow.Presentation.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace SeatFlow.Presentation.Avalonia
{
    /// <summary>
    /// Given a view model, returns the corresponding view if possible.
    /// M3：按 ViewModel 实例缓存已构造的 View，避免每次切页重复 XAML 构建（切页 INP 优化）。
    /// 使用 <see cref="ConditionalWeakTable{TKey,TValue}"/> 弱键，Transient 页面（如快照）离开后可回收。
    /// </summary>
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away.",
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
    public class ViewLocator : IDataTemplate
    {
        private readonly ConditionalWeakTable<ViewModelBase, Control> _viewCache = new();

        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            if (param is ViewModelBase vm && _viewCache.TryGetValue(vm, out var cached))
                return cached;

            var fullName = param.GetType().FullName;
            if (fullName is null) return null;

            var name = fullName.Replace("ViewModel", "View", StringComparison.Ordinal);
            var type = Type.GetType(name);

            if (type != null && Activator.CreateInstance(type) is Control control)
            {
                control.DataContext = param;
                if (param is ViewModelBase viewModel)
                    _viewCache.Add(viewModel, control);
                return control;
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
