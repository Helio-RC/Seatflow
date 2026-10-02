import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

// M5：预加载卫星资源程序集（en-US 等）。WASM 运行时默认按需加载卫星程序集，
// 但独立 WASM（非 Blazor）的按需解析无法定位 _framework/{culture}/ 下的资源文件，
// 导致运行时切换语言（Resources.Culture）永远回退到中性资源（中文）。
// 通过 loadAllSatelliteResources 让运行时把 satelliteResources 注册进资源清单。
const dotnetRuntime = await dotnet
    .withConfig({ loadAllSatelliteResources: true })
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
