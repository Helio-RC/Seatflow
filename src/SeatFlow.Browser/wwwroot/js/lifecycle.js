// SeatFlow 浏览器端生命周期桥（模块名 "sf.lifecycle"）：
//   页面关闭 / 刷新前的未保存提示。C# 侧定期同步当前页面脏状态；
//   beforeunload 只能同步判断，因此用本模块的内存标志镜像。
let hasUnsavedChanges = false;

export function setUnsavedChanges(value) {
  hasUnsavedChanges = !!value;
}

if (typeof window !== "undefined" && !window.__seatflowBeforeUnloadGuard) {
  window.__seatflowBeforeUnloadGuard = true;
  window.addEventListener("beforeunload", (event) => {
    if (!hasUnsavedChanges) return;
    event.preventDefault();
    // 兼容旧版浏览器：设置非空返回值才会显示确认框。
    event.returnValue = "SeatFlow";
    return "SeatFlow";
  });
}
