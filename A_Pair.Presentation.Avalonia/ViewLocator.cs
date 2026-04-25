using System;
using System.Diagnostics.CodeAnalysis;
using A_Pair.Presentation.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace A_Pair.Presentation.Avalonia
{
    /// <summary>
    /// Given a view model, returns the corresponding view if possible.
    /// </summary>
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away." ,
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
    public class ViewLocator : IDataTemplate
    {
        public Control? Build (object? param)
        {
            if (param is null)
                return null;

            var name = param.GetType().FullName!;
            // first try the simple replacement (same namespace as ViewModel)
            var tryName = name.Replace("ViewModel" , "View" , StringComparison.Ordinal);
            var type = Type.GetType(tryName);

            if (type == null)
            {
                // common convention: views live in a .Views namespace instead of .ViewModels
                var alt = tryName.Replace(".ViewModels." , ".Views." , StringComparison.Ordinal);
                type = Type.GetType(alt);
                tryName = alt;
            }

            if (type != null)
            {
                return (Control)Activator.CreateInstance(type)!;
            }

            return new TextBlock { Text = "Not Found: " + tryName };
        }

        public bool Match (object? data)
        {
            return data is ViewModelBase;
        }
    }
}
