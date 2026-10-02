// SeatFlow 浏览器端文件互操作桥（模块名 "sf.files"）：
//   sfPickFile(accept) -> JSON { name, b64 } | null   （打开文件选择）
//   sfDownloadFile(name, b64)                        （触发浏览器下载）

function base64ToBytes(b64) {
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

function bytesToBase64(bytes) {
  let bin = "";
  for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
  return btoa(bin);
}

export function sfPickFile(accept) {
  return new Promise((resolve) => {
    const input = document.createElement("input");
    input.type = "file";
    if (accept) input.accept = accept;
    let settled = false;
    const finish = (value) => {
      if (settled) return;
      settled = true;
      input.remove();
      resolve(value);
    };
    input.onchange = async () => {
      try {
        const file = input.files && input.files[0];
        if (!file) { finish(null); return; }
        const buf = await file.arrayBuffer();
        finish(JSON.stringify({ name: file.name, b64: bytesToBase64(new Uint8Array(buf)) }));
      } catch (err) {
        // 读取失败时不要悬挂 Promise（否则调用方永久等待）
        console.error('sfPickFile: 读取所选文件失败', err);
        finish(null);
      }
    };
    // 现代浏览器支持 cancel 事件（用户直接关闭选择框）：避免 Promise 悬挂并清理节点
    input.addEventListener("cancel", () => finish(null));
    // 注意：input 必须保留在 DOM 中直到选择/取消（立即 remove 会导致部分浏览器不弹选择框）
    document.body.appendChild(input);
    input.click();
  });
}

// 注意：JSImport 声明为 Task，JS 函数必须返回 Promise（async），否则互操作层会 NRE。
export async function sfDownloadFile(fileName, base64Content) {
  const bytes = base64ToBytes(base64Content);
  const blob = new Blob([bytes], { type: "application/octet-stream" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName || "download";
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 5000);
}
