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
    input.onchange = async () => {
      const file = input.files && input.files[0];
      if (!file) { resolve(null); return; }
      const buf = await file.arrayBuffer();
      resolve(JSON.stringify({ name: file.name, b64: bytesToBase64(new Uint8Array(buf)) }));
    };
    // Safari 兼容：追加后立刻移除
    document.body.appendChild(input);
    input.click();
    input.remove();
  });
}

export function sfDownloadFile(fileName, base64Content) {
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
