using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using A_Pair.Presentation.Avalonia.Lang;
using CommunityToolkit.Mvvm.Input;
using AvaloniaApplication = Avalonia.Application;

namespace A_Pair.Presentation.Avalonia.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public string AppName { get; } = "A_Pair";
    public string Version { get; }
    public string VersionDisplay { get; }
    public string Description { get; }
    public string RuntimeVersion { get; }
    public string AvaloniaVersion { get; }
    public string ProjectUrl { get; }
    public string License { get; }
    public string Copyright { get; }

    public List<DependencyInfo> Dependencies { get; }

    public AboutViewModel ()
    {
        var data = LoadAboutData();

        var assembly = Assembly.GetEntryAssembly();
        Version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? assembly?.GetName()?.Version?.ToString()
                  ?? "1.0.0";
        VersionDisplay = string.Format(Resources.About_Version , Version);

        RuntimeVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        AvaloniaVersion = typeof(AvaloniaApplication).Assembly.GetName().Version?.ToString() ?? "";

        Description = data.Description;
        ProjectUrl = data.ProjectUrl;
        License = data.License;
        Copyright = data.Copyright;
        Dependencies = data.Dependencies
            .Select(d => new DependencyInfo(d.Name, d.Version, d.Purpose, d.License, d.Url))
            .ToList();
    }

    [RelayCommand]
    private void OpenUrl (string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static AboutData LoadAboutData ()
    {
        var assembly = typeof(AboutViewModel).Assembly;
        const string resourceName = "A_Pair.Presentation.Avalonia.Data.about.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"嵌入式资源未找到: {resourceName}");
        return JsonSerializer.Deserialize<AboutData>(stream) ?? new AboutData();
    }

    private sealed class AboutData
    {
        public string Description { get; set; } = "";
        public string ProjectUrl { get; set; } = "";
        public string License { get; set; } = "";
        public string Copyright { get; set; } = "";
        public List<DepEntry> Dependencies { get; set; } = [];
    }

    private sealed class DepEntry
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string License { get; set; } = "";
        public string Url { get; set; } = "";
    }
}

public class DependencyInfo
{
    public string Name { get; }
    public string Version { get; }
    public string Purpose { get; }
    public string License { get; }
    public string Url { get; }

    public DependencyInfo (string name , string version , string purpose , string license , string url)
    {
        Name = name;
        Version = version;
        Purpose = purpose;
        License = license;
        Url = url;
    }
}
