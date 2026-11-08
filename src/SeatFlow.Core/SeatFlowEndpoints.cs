namespace SeatFlow.Core;

/// <summary>
/// 后端 API 端点。**地址唯一来源**，勿在别处硬编码域名。<br/>
/// 客户端全部请求走 <see cref="ApiBase"/>；apex（seatflow.work）只是过渡期的 Worker 反代，不是客户端入口。
/// </summary>
public static class SeatFlowEndpoints
{
    /// <summary>API 基址（带尾斜杠，可直接作为 HttpClient.BaseAddress）。</summary>
    public const string ApiBase = "https://api.seatflow.work/api/";

    /// <summary>遥测上报端点。</summary>
    public const string TelemetryUrl = ApiBase + "app/telemetry";

    /// <summary>版本更新说明端点（后接版本号）。</summary>
    public const string ReleaseNotesUrl = ApiBase + "app/note/";

    /// <summary>在线版（WASM）启动计数端点。</summary>
    public const string OnlineLaunchUrl = ApiBase + "app/online/launch";
}
