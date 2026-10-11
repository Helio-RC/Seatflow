import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

// ── 启动加载页（index.html 中的 .avalonia-splash）───────────────────────────
// 首次打开需下载约 18 MB 应用资源（.br 预压缩），按运行时回调显示进度；
// Avalonia 接管 #out 时运行时添加 .splash-close 类自动隐藏加载页。
const splash = document.getElementById('avalonia-splash');
const statusEl = document.getElementById('sfStatus');
const hintEl = document.getElementById('sfHint');
const trackEl = document.getElementById('sfTrack');
const barEl = document.getElementById('sfBar');

const isZh = (navigator.language || '').toLowerCase().startsWith('zh');
if (document.documentElement) document.documentElement.lang = isZh ? 'zh-CN' : 'en';

const text = isZh
    ? {
        loading: '正在加载 SeatFlow…',
        progress: (loaded, total) => `正在下载应用资源 ${loaded}/${total}`,
        ready: '正在启动应用…',
        error: '加载失败，请检查网络后刷新重试',
        hint: '首次打开需要下载约 18 MB 应用资源，之后访问将直接从缓存秒开',
        progressLabel: '加载进度',
    }
    : {
        loading: 'Loading SeatFlow…',
        progress: (loaded, total) => `Downloading resources ${loaded}/${total}`,
        ready: 'Starting up…',
        error: 'Failed to load. Check your connection and refresh.',
        hint: 'The first visit downloads about 18 MB of resources; later visits load from cache instantly.',
        progressLabel: 'Loading progress',
    };

// 进度条取「高水位」单调推进：运行时的分母是动态增长的已发起下载数，
// 直接取 loaded/total 会在批次边界回跳（已实测 67% → 53%）。
let maxFraction = 0;
let readyTimer = 0;

function setSplashState(state) {
    if (splash) splash.dataset.state = state;
}

function showProgress(loaded, total) {
    if (!statusEl || !trackEl || !barEl || total <= 0) return;

    const fraction = Math.max(0, Math.min(1, loaded / total));
    if (fraction > maxFraction) maxFraction = fraction;
    barEl.style.transform = `scaleX(${maxFraction})`;
    trackEl.setAttribute('aria-valuenow', String(Math.round(maxFraction * 100)));

    if (fraction >= 1) {
        // 批次间隙也可能短暂满格：静默 500ms 无新回调才进入启动阶段
        if (!readyTimer) {
            readyTimer = window.setTimeout(() => {
                readyTimer = 0;
                statusEl.textContent = text.ready;
                setSplashState('ready');
            }, 500);
        }
        return;
    }

    if (readyTimer) {
        window.clearTimeout(readyTimer);
        readyTimer = 0;
    }
    statusEl.textContent = text.progress(loaded, total);
    setSplashState('progress');
}

function showError() {
    if (readyTimer) {
        window.clearTimeout(readyTimer);
        readyTimer = 0;
    }
    if (splash) splash.classList.remove('splash-close');
    if (barEl) barEl.style.transform = 'scaleX(1)';
    if (trackEl) trackEl.removeAttribute('aria-valuenow');
    if (statusEl) statusEl.textContent = text.error;
    setSplashState('error');
}

if (statusEl) statusEl.textContent = text.loading;
if (hintEl) hintEl.textContent = text.hint;
if (trackEl) trackEl.setAttribute('aria-label', text.progressLabel);

try {
    // M5：预加载卫星资源程序集（en-US 等）。WASM 运行时默认按需加载卫星程序集，
    // 但独立 WASM（非 Blazor）的按需解析无法定位 _framework/{culture}/ 下的资源文件，
    // 导致运行时切换语言（Resources.Culture）永远回退到中性资源（中文）。
    // 通过 loadAllSatelliteResources 让运行时把 satelliteResources 注册进资源清单。
    let builder = dotnet
        .withConfig({ loadAllSatelliteResources: true })
        .withDiagnosticTracing(false)
        .withApplicationArgumentsFromQuery();

    // onDownloadResourceProgress 属模块配置（DotnetModuleConfig）；做特性探测，
    // 不可用时仅退化为不确定态动画，不影响启动。
    if (typeof builder.withModuleConfig === 'function') {
        builder = builder.withModuleConfig({
            onDownloadResourceProgress: (loaded, total) => showProgress(loaded, total),
        });
    }

    const dotnetRuntime = await builder.create();

    const config = dotnetRuntime.getConfig();

    await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
} catch (error) {
    console.error('SeatFlow 启动失败:', error);
    showError();
}
